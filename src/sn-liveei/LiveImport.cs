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

        public static async System.Threading.Tasks.Task ImportContentAsync()
        {
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

            await ImportContentsAsync(fsPath, targetRepoPath, validate);
        }

        private static async System.Threading.Tasks.Task ImportContentsAsync(string srcPath, string targetPath, bool validate)
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
            Log.Information("==============================================================");

            if (targetPath != null)
            {
                importTarget = await Content.LoadAsync(targetPath);
                if (importTarget == null)
                {
                    Log.Warning("Target container was not found: ");
                    Log.Information(targetPath);
                    return;
                }
            } else
            {
                await Content.LoadAsync("/Root");
            }

            try
            {
                List<ContentInfo> postponedList = new List<ContentInfo>();
                await TreeWalkerAsync(srcPath, pathIsFile, importTarget, "  ", postponedList, validate);
            }
            catch (Exception e)
            {
                Log.Error(e, null);
            }

            Log.Information("========================================");
        }

        private static async System.Threading.Tasks.Task TreeWalkerAsync(string path, bool pathIsFile, Content folder, string indent, List<ContentInfo> postponedList, bool validate)
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

                bool isNewContent = true;
                Content content = null;
                try
                {
                    content = await CreateOrLoadContentAsync(contentInfo, folder, isNewContent);
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

                        await content.SaveAsync();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"{e.Message}, {contentInfo.MetaDataPath}");
                        return;
                    }
                }

                //-- recursion
                if (stepDown)
                {
                    if (contentInfo.IsFolder)
                    {
                        if (content != null)
                            await TreeWalkerAsync(contentInfo.ChildrenFolder, false, content, indent + "  ", postponedList, validate);
                    }
                }
            }
        }

        private static async System.Threading.Tasks.Task<Content> CreateOrLoadContentAsync(ContentInfo contentInfo, Content targetRepoParent, bool isNewContent)
        {
            string path = RepositoryPath.Combine(targetRepoParent.Path, contentInfo.Name);
            Content content = await Content.LoadAsync(path);
            if (content == null)
            {
                // need list of nodetypes descended from file
                if (!fileTypes.Any(f => f == contentInfo.ContentTypeName))
                {
                    content = Content.CreateNew(targetRepoParent.Path, contentInfo.ContentTypeName, contentInfo.Name);
                } 
                else
                {
                    using (FileStream fs = File.OpenRead(contentInfo.MetaDataPath))
                    {
                        content = await Content.UploadAsync(targetRepoParent.Path, contentInfo.Name, fs, contentInfo.ContentTypeName);
                    }
                }
                isNewContent = true;
            }
            else
            {
                isNewContent = false;
            }

            return content;
        }
    }
}
