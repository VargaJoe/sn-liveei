using SenseNet.Client;
using Serilog;
using SnLiveExportImport.ContentImporter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SnLiveExportImport
{
    public static class LiveImport
    {
        public static string ContentTypesFolderPath = "/Root/System/Schema/ContentTypes";
        public static string[] fileTypes = { "File", "Image" };
        private static string continueFrom; 

        public static void ImportContent()
        {
            CreateRefLog(continueFrom == null);

            // TODO: get variables from parameters and/or settings
            string targetRepoParentPath = "/Root";
            string targetRepoPath = "/Root/Content";
            string sourceBasePath = "./import"; 
            bool sync = true; 
            bool validate = false;

            string fsTargetRepoPath = $".{targetRepoPath}";
            string cbPath = (sync) ? Path.Combine(sourceBasePath, fsTargetRepoPath) : sourceBasePath;
            string fsPath = Path.GetFullPath(cbPath);
            
            Log.Information($"target parent path: {targetRepoParentPath}");
            Log.Information($"target repo path: {targetRepoPath}");
            Log.Information($"source path: {sourceBasePath}");
            Log.Information($"sync: {sync}");
            Log.Information($"combined path: {cbPath}");
            Log.Information($"filesystem path: {fsPath}");

            if (sync && File.Exists(fsPath + ".Content"))
            {
                Log.Information($"file exists: {fsPath}.Content");

                fsPath += ".Content";
                targetRepoPath = targetRepoParentPath; 
                Log.Information($"target repo path changed: {targetRepoPath}");
            } else
            {
                Log.Information($"file does not exists: {fsPath}.Content");
            }

            ImportContents(fsPath, targetRepoPath, validate);
        }

        private static void ImportContents(string srcPath, string targetPath, bool validate)
        {
            bool pathIsFile = false;
            if (File.Exists(srcPath))
            {
                pathIsFile = true;
            }
            else if (!Directory.Exists(srcPath))
            {
                Log.Information("Source directory or file was not found: ");
                Log.Information(srcPath);
                return;
            }

            Content importTarget = null;
            Log.Information("");
            Log.Information("=================== Continuing Import ========================");
            Log.Information($"From: {srcPath}" );
            Log.Information($"To:   {targetPath}");
            //if (_continueFrom != null)
            //    Log.Information($"Continuing from: {_continueFrom}");
            //if (!validate)
            //    Log.Information("Content validation: OFF");
            Log.Information("==============================================================");

            if (targetPath != null)
            {
                importTarget = Content.LoadAsync(targetPath).GetAwaiter().GetResult();
                if (importTarget == null)
                {
                    Log.Warning("Target container was not found: ");
                    Log.Information(targetPath);
                    return;
                }
            }

            try
            {
                // list for tasks after content import
                List<ContentInfo> postponedList = new List<ContentInfo>();

                // first round create or update contents 
                TreeWalker(srcPath, pathIsFile, importTarget, "  ", postponedList, validate);

                // after all contents are imported can references updated
                if (postponedList.Count != 0)
                    UpdateReferences(/*postponedList*/validate);

                //foreach (var site in sites.Nodes)
                //{
                //    site.Security.SetPermission(User.Visitor, true, PermissionType.RunApplication, PermissionValue.Allow);
                //}

            }
            catch (Exception e)
            {
                PrintException(e, null);
            }

            Log.Information("========================================");
        }

        private static void TreeWalker(string path, bool pathIsFile, Content folder, string indent, List<ContentInfo> postponedList, bool validate)
        {

            if (!string.IsNullOrWhiteSpace(folder.Path) && folder.Path.StartsWith(ContentTypesFolderPath))
            {
                //-- skip CTD folder
                Log.Information($"Skipped path: {path}");
                return;
            }

            string currentDir = pathIsFile ? Path.GetDirectoryName(path) : path;
            List<ContentInfo> contentInfos = new List<ContentInfo>();
            List<string> paths;
            List<string> contentPaths;
            if (pathIsFile)
            {
                paths = new List<string>(new string[] { path });
                contentPaths = new List<string>();
                if (path.ToLower().EndsWith(".content"))
                    contentPaths.Add(path);
            }
            else
            {
                paths = new List<string>(Directory.GetFileSystemEntries(path));
                contentPaths = new List<string>(Directory.GetFiles(path, "*.content"));
            }

            foreach (string contentPath in contentPaths)
            {
                paths.Remove(contentPath);
                var contentInfo = new ContentInfo(contentPath);
                if (!contentInfo.IsHidden)
                    contentInfos.Add(contentInfo);
                foreach (string attachmentName in contentInfo.Attachments)
                    paths.Remove(Path.Combine(path, attachmentName));
            }
            while (paths.Count > 0)
            {
                var contentInfo = new ContentInfo(paths[0]);
                if (!contentInfo.IsHidden)
                    contentInfos.Add(contentInfo);
                paths.RemoveAt(0);
            }

            foreach (ContentInfo contentInfo in contentInfos)
            {
                var continuing = false;
                var stepDown = true;
                if (continueFrom != null)
                {
                    continuing = true;
                    if (contentInfo.MetaDataPath == continueFrom)
                    {
                        continueFrom = null;
                        continuing = false;
                    }
                    else
                    {
                        stepDown = continueFrom.StartsWith(contentInfo.MetaDataPath);
                    }
                }

                bool isNewContent = true;
                Content content = null;
                try
                {
                    content = CreateOrLoadContent(contentInfo, folder, isNewContent);
                } 
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                }

                if (content == null)
                {
                    Log.Error($"content is null: {contentInfo.Name}");
                }

                isNewContent = string.IsNullOrWhiteSpace(content.Path);
                if (!continuing)
                {
                    string newOrUpdate = isNewContent ? "[new]" : "[update]";
                    
                    Log.Information($"{indent} {contentInfo.Name} : {contentInfo.ContentTypeName} {newOrUpdate} ");

                    //-- SetMetadata without references. Return if the setting is false or exception was thrown.
                    try
                    {
                        var setResult = contentInfo.SetMetadata(content, currentDir, isNewContent, validate, false);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"{e.Message}, {e.InnerException?.Message}, {contentInfo.MetaDataPath}");
                        return;
                    }

                    // gather security change iformation
                    if (contentInfo.ClearPermissions)
                    {
                        // here should RemoveExplicitEntries from content
                        if (!(contentInfo.HasReference || contentInfo.HasPermissions || contentInfo.HasBreakPermissions))
                        {
                            // here should RemoveBreakInheritance from content
                        }
                    }
                    if (contentInfo.HasReference || contentInfo.HasPermissions || contentInfo.HasBreakPermissions)
                    {
                        LogWriteReference(contentInfo);
                        postponedList.Add(contentInfo);
                    }
                }

                //-- recursion
                if (stepDown)
                {
                    if (contentInfo.IsFolder)
                    {
                        if (content != null)
                            TreeWalker(contentInfo.ChildrenFolder, false, content, indent + "  ", postponedList, validate);
                    }
                }
            }
        }

        private static Content CreateOrLoadContent(ContentInfo contentInfo, Content targetRepoParent, bool isNewContent)
        {
            string path = RepositoryPath.Combine(targetRepoParent.Path, contentInfo.Name);
            isNewContent = false;
            Content content = Content.LoadAsync(path).GetAwaiter().GetResult();
            if (content == null)
            {
                // need list of nodetypes descended from file
                if (!fileTypes.Any(f => f == contentInfo.ContentTypeName))
                {
                    content = Content.CreateNew(targetRepoParent.Path, contentInfo.ContentTypeName, contentInfo.Name);
                    isNewContent = true;
                } 
                else
                {
                    using (FileStream fs = File.OpenRead(contentInfo.MetaDataPath))
                    {
                        content = Content.UploadAsync(targetRepoParent.Path, contentInfo.Name, fs, contentInfo.ContentTypeName).GetAwaiter().GetResult();
                    }
                }                
            }

            return content;
        }

        private static void UpdateReferences(bool validate)
        {
            LogWriteLine("=========================== Update references");

            var idList = new List<int>();
            using (var reader = new StreamReader(_refLogFilePath))
            {
                while (!reader.EndOfStream)
                {
                    var s = reader.ReadLine();
                    var sa = s.Split('\t');
                    var id = int.Parse(sa[0]);
                    var path = sa[1];
                    if (idList.Contains(id))
                        continue;
                    UpdateReference(id, path, validate);
                    idList.Add(id);
                }
            }

            //LogWriteLine();
        }
        private static void UpdateReference(int contentId, string metadataPath, bool validate)
        {
            var contentInfo = new ContentInfo(metadataPath);

            LogWriteLine($"  {contentInfo.Name}");

            Content content = Content.LoadAsync(contentId).GetAwaiter().GetResult();
            if (content != null)
            {
                try
                {
                    if (contentInfo.UpdateReferences(content, validate))
                    {
                        content.SaveAsync().GetAwaiter().GetResult();
                    }
                }
                catch (Exception e)
                {
                    PrintException(e, contentInfo.MetaDataPath);
                }
            }
            else
            {
                LogWriteLine($"---------- " +
                    $"Content does not exist. MetaDataPath: {contentInfo.MetaDataPath}, " +
                    $"ContentId: {contentInfo.ContentId}, " +
                    $"ContentTypeName: {contentInfo.ContentTypeName}");
            }
        }

        private static string _refLogFilePath;//= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "import.reflog");

        public static void LogWriteReference(ContentInfo contentInfo)
        {
            using (StreamWriter writer = new StreamWriter(_refLogFilePath, true))
            {
                WriteToRefLog(writer, contentInfo.ContentId, '\t', contentInfo.MetaDataPath);
            }
        }
        private static void CreateRefLog(bool createNew)
        {
            _refLogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "import.reflog");
            if (!File.Exists(_refLogFilePath) || createNew)
            {
                using (FileStream fs = new FileStream(_refLogFilePath, FileMode.Create))
                {
                    using (StreamWriter wr = new StreamWriter(fs))
                    {
                        // do nothing
                    }
                }
            }
        }
        private static void WriteToRefLog(StreamWriter writer, params object[] values)
        {
            foreach (object value in values)
            {
                //Console.Write(value);
                writer.Write(value);
            }
            //Console.WriteLine();
            writer.WriteLine();
        }
        private static void CloseLog(StreamWriter writer)
        {
            writer.Flush();
            writer.Close();
        }

        //Start of Logging
        private static void LogWriteLine(params object[] path)
        {
            Log.Information(string.Concat(string.Join(",", path)));
        }

        private static void PrintException(Exception e, string path)
        {
            //exceptions++;
            Log.Error("========== Exception:");
            if (!String.IsNullOrEmpty(path))
            {
                Log.Error($"Path: {path}");
                Log.Error("---------------------");
            }

            WriteEx(e);
            while ((e = e.InnerException) != null)
            {
                Log.Error("---- Inner Exception:");
                WriteEx(e);
            }
            Log.Error("=====================");
        }

        private static void WriteEx(Exception e)
        {
            Log.Error($"{e.GetType().Name}: {e.Message}");
            Log.Error(e.StackTrace);
        }

        ////End of Logging
    }
}
