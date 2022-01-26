using SenseNet.Client;
using Serilog;
using SnLiveExportImport.ContentImporter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace SnLiveExportImport
{
    public static class LiveImport
    {
        public static void ImportContent()
        {
            CreateRefLog(string.IsNullOrEmpty(Program._appConfig.ContinueFrom));

            // TODO: get variables from parameters and/or settings
            int lastSlash = Program._appConfig.RepoPath.LastIndexOf("/");
            string targetRepoParentPath = Program._appConfig.RepoPath.Substring(0, lastSlash);
            string targetRepoPath = Program._appConfig.RepoPath;
            string sourceBasePath = Program._appConfig.LocalPath;
            bool syncmode = Program._appConfig.SyncMode; 
            bool validate = false;

            //string fsTargetRepoPath = $".{targetRepoPath.Replace("/Root", "")}";
            string fsTargetRepoPath = $".{targetRepoPath}";
            string cbPath = (syncmode) ? Path.Combine(sourceBasePath, fsTargetRepoPath) : sourceBasePath;
            string fsPath = Path.GetFullPath(cbPath);

            // content type folder expected
            string ctPath = Path.Combine(cbPath, "System/Schema/ContentTypes");
            string fsCtPath = Path.GetFullPath(ctPath);
            ImportContentTypeDefinitionsAndAspects(fsCtPath, null);

            Log.Information($"target parent path: {targetRepoParentPath}");
            Log.Information($"target repo path: {targetRepoPath}");
            Log.Information($"source path: {sourceBasePath}");
            Log.Information($"sync: {syncmode}");
            Log.Information($"combined path: {cbPath}");
            Log.Information($"filesystem path: {fsPath}");

            if (syncmode && File.Exists(fsPath + ".Content"))
            {
                Log.Information($"file exists: {fsPath}.Content");

                fsPath += ".Content";
                targetRepoPath = targetRepoParentPath; 
                Log.Information($"target repo path changed: {targetRepoPath}");
            } else
            {
                Log.Information($"file does not exists: {fsPath}.Content");
            }

            if (!string.IsNullOrWhiteSpace(targetRepoPath))
            {
                var isTargetExists = Content.ExistsAsync(targetRepoPath).GetAwaiter().GetResult();
                if (!isTargetExists)
                {
                    Log.Warning($"Target container was not found: {targetRepoPath}");
                    return;
                }
            }

            //SenseNet.Client.Importer.ImportAsync(fsPath, targetRepoPath).GetAwaiter().GetResult();

            ImportContents(fsPath, targetRepoPath, validate);
        }

        public static void ImportContentTypeDefinitionsAndAspects(string ctdPath, string aspectsPath)
        {
            if (ctdPath != null && Directory.Exists(ctdPath))
            {
                Log.Information($"Importing content types: {ctdPath}");

                //ContentTypeInstaller importer = ContentTypeInstaller.CreateBatchContentTypeInstaller();
                var ctdFiles = Directory.GetFiles(ctdPath, "*.xml");
                foreach (var ctdFilePath in ctdFiles)
                {

                    var ctdName = Path.GetFileNameWithoutExtension(ctdFilePath);

                    // workaround, name should get from xml 
                    // TODO: recursive import ctds with existing parents 
                    if (ctdName.EndsWith("Ctd"))
                    {
                        var lastCtdPos = ctdName.LastIndexOf("Ctd");
                        ctdName = ctdName.Substring(0, lastCtdPos);
                    }

                    using (var fStream = new FileStream(ctdFilePath, FileMode.Open, FileAccess.Read))
                    {
                        try
                        {
                            Log.Information($"  {Path.GetFileName(ctdFilePath)}");
                            //importer.AddContentType(stream);
                            Thread.Sleep(1000);
                            var ctdContent = Content.UploadAsync("/Root/System/Schema/ContentTypes", ctdName, fStream, "ContentType").GetAwaiter().GetResult();
                            //Thread.Sleep(100);
                        }
                        catch (ApplicationException e)
                        {
                            //Logger.Errors++;
                            Log.Error($"  SKIPPED: {e.Message}");
                        }
                    }
                }
                Log.Information($"  {ctdFiles.Length} file loaded...");

                //using (CreateProgressBar())
                //    importer.ExecuteBatch();

                Log.Information($"  {ctdFiles.Length} CTD imported.");
                Log.Information("Ok");
            }
            else
            {
                Log.Information("CTDs not changed");
            }

            // ==============================================================

            if (aspectsPath != null && Directory.Exists(aspectsPath))
            {
                //if (!Node.Exists(Repository.AspectsFolderPath))
                //{
                //    Log(ImportLogLevel.Info, "Creating aspect container (" + Repository.AspectsFolderPath + ")...");
                //    Content.CreateNew(typeof(SystemFolder).Name, Repository.SchemaFolder, "Aspects").Save();
                //    Log(ImportLogLevel.Info, "  Ok");
                //}

                //var aspectFiles = System.IO.Directory.GetFiles(aspectsPath, "*.content");
                //Log(ImportLogLevel.Info, "Importing aspects:");

                //ImportContents(aspectsPath, Repository.AspectsFolderPath, true, false);

                //Log(ImportLogLevel.Info, "  " + aspectFiles.Length + " aspect" + (aspectFiles.Length > 1 ? "s" : "") + " imported.");
                //Log(ImportLogLevel.Progress, "Ok");
            }
            else
            {
                Log.Information("Aspects not changed.");
            }
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

            Log.Information("");
            Log.Information("=================== Continuing Import ========================");
            Log.Information($"From: {srcPath}" );
            Log.Information($"To:   {targetPath}");
            //if (_continueFrom != null)
            //    Log.Information($"Continuing from: {_continueFrom}");
            //if (!validate)
            //    Log.Information("Content validation: OFF");
            Log.Information("==============================================================");

            try
            {
                // list for tasks after content import
                List<ContentInfo> postponedList = new List<ContentInfo>();

                // first round create or update contents 
                TreeWalker(srcPath, pathIsFile, targetPath, "  ", postponedList, validate);

                // after all contents are imported can references updated
                if (postponedList.Count != 0)
                    UpdateReferences(/*postponedList*/validate);
            }
            catch (Exception e)
            {
                PrintException(e, null);
                //Thread.Sleep(1000);
            }

            Log.Information("========================================");
        }

        private static void TreeWalker(string path, bool pathIsFile, string parentPath, string indent, List<ContentInfo> postponedList, bool validate)
        {

            if (!string.IsNullOrWhiteSpace(parentPath) && parentPath.StartsWith(Program._appConfig.ContentTypesFolderPath))
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
                if (Program._appConfig.ContinueFrom != null)
                {
                    continuing = true;
                    if (contentInfo.MetaDataPath == Program._appConfig.ContinueFrom)
                    {
                        Program._appConfig.ContinueFrom = null;
                        continuing = false;
                    }
                    else
                    {
                        stepDown = Program._appConfig.ContinueFrom.StartsWith(contentInfo.MetaDataPath);
                    }
                }

                if (contentInfo.ContentTypeName == "ContentType")
                {
                    Log.Warning($"ContentType import is not allowed outside Schema folder, check your settings or import package! Skipped: {contentInfo.MetaDataPath}, {parentPath}");
                    continue;
                }

                bool isNewContent = true;
                Content content = null;
                try
                {
                    content = CreateOrLoadContent(contentInfo, parentPath, ref isNewContent);
                } 
                catch (Exception ex)
                {
                    Log.Error($"Exception on Load/Create, content: {contentInfo.MetaDataPath}, {ex.Message}, {ex.InnerException?.Message}");
                    //Thread.Sleep(1000);
                    continue;
                }

                if (content == null)
                {
                    Log.Error($"After Load/Create content is null: {contentInfo.MetaDataPath}");
                    continue;
                }

                //isNewContent = string.IsNullOrWhiteSpace(content.Path);
                if (!continuing)
                {
                    // TODO: show if file uploaded
                    string newOrUpdate = isNewContent ? "[new]" : "[update]";

                    Log.Information($"{indent} {contentInfo.Name} : {contentInfo.ContentTypeName} {newOrUpdate}");

                    //-- SetMetadata without references. Return if the setting is false or exception was thrown.
                    try
                    {
                        Thread.Sleep(500);
                        var setResult = contentInfo.SetMetadata(content, currentDir, isNewContent, validate, false);

                        // strange error
                        var realContentType = content["Type"]?.ToString();
                        if (contentInfo.ContentTypeName != realContentType)
                        {
                            // TODO: check why are there null contenttypes after uploadadync 
                            //Log.Error($"ContentType mismatch! Repo: '{realContentType}', Import source: '{contentInfo.ContentTypeName}'");
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Error at SetMetaData: {contentInfo.MetaDataPath}, {e.Message} {e.InnerException?.Message}");
                        //Thread.Sleep(1000);
                        continue;
                    }
                    
                    // gather security change iformation
                    if (contentInfo.ClearPermissions)
                    {
                        // here should RemoveExplicitEntries from content
                        // TODO: how?

                        if (!(contentInfo.HasReference || contentInfo.HasPermissions || contentInfo.HasBreakPermissions))
                        {
                           // here should RemoveBreakInheritance from content
                           content.UnbreakInheritanceAsync();
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
                            TreeWalker(contentInfo.ChildrenFolder, false, content.Path, indent + "  ", postponedList, validate);
                    }
                }
                //Thread.Sleep(100);
            }
        }

        private static Content CreateOrLoadContent(ContentInfo contentInfo, string targetRepoParentPath, ref bool isNewContent)
        {
            string path = RepositoryPath.Combine(targetRepoParentPath, contentInfo.Name);
            isNewContent = false;
            Content content = Content.LoadAsync(path).GetAwaiter().GetResult();
            if (content == null)
            {
                // need list of nodetypes descended from file
                if (!Program._appConfig.FileTypes.Any(f => f == contentInfo.ContentTypeName))
                {
                    content = Content.CreateNew(targetRepoParentPath, contentInfo.ContentTypeName, contentInfo.Name);
                    isNewContent = true;
                }
                else
                {
                    string filePath = contentInfo.Attachments.Count > 0 ? Path.Combine(Path.GetDirectoryName(contentInfo.MetaDataPath), contentInfo.Attachments[0]) : contentInfo.MetaDataPath;
                    using (FileStream fs = File.OpenRead(filePath))
                    {
                        Thread.Sleep(1000);
                        content = Content.UploadAsync(targetRepoParentPath, contentInfo.Name, fs, contentInfo.ContentTypeName).GetAwaiter().GetResult();
                        //Thread.Sleep(100);
                        isNewContent = true;
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

                    //Thread.Sleep(1000);
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
                        Thread.Sleep(100);
                    }
                }
                catch (Exception e)
                {
                    PrintException(e, contentInfo.MetaDataPath);
                    //Thread.Sleep(1000);
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
