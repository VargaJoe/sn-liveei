using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using SenseNet.Client;
using Microsoft.Extensions.Configuration;
using Serilog;
using SenseNet.Client.Security;
using System.Threading;
using System.Linq;
using System.Xml.Xsl;

namespace SnLiveExportImport.ContentImporter
{
    [DebuggerDisplay("ContentInfo: Name={Name}; ContentType={ContentTypeName}; IsFolder={IsFolder} ({Attachments.Count} Attachments)")]
    public class ContentInfo
    {
        private string _metaDataPath;
        private int _contentId;
        private bool _isFolder;
        private string _name;
        private List<string> _attachments;
        private string _contentTypeName;
        private XmlDocument _xmlDoc;
        private string _childrenFolder;
        private ImportContext _transferringContext;
        private static readonly object _xsltLock = new object();
        private static XslCompiledTransform _importXslt;
        private static bool _importXsltInitialized;

        public string MetaDataPath
        {
            get { return _metaDataPath; }
        }
        public int ContentId
        {
            get { return _contentId; }
        }
        public bool IsFolder
        {
            get { return _isFolder; }
        }
        public string Name
        {
            get { return _name; }
        }
        public List<string> Attachments
        {
            get { return _attachments; }
        }
        public string ContentTypeName
        {
            get { return _contentTypeName; }
        }
        public string ChildrenFolder
        {
            get { return _childrenFolder; }
        }
        public bool HasReference
        {
            get
            {
                if (_transferringContext == null)
                    return false;
                return _transferringContext.HasReference;
            }
        }
        public bool HasPermissions { get; private set; }
        public bool HasBreakPermissions { get; private set; }
        public bool ClearPermissions { get; private set; }
        public bool IsHidden { get; private set; }

        public static IConfigurationRoot _configuration;
        public static IConfigurationRoot Configuration
        {
            get
            {
                if (_configuration == null)
                {
                    // TODO: use global configuration
                    _configuration = new ConfigurationBuilder()
                      .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
                      .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                      .AddEnvironmentVariables().Build();
                }

                return _configuration;
            }
        }

        public ContentInfo(string path)
        {
            try
            {
                _metaDataPath = path;
                _attachments = new List<string>();

                string directoryName = Path.GetDirectoryName(path);
                _name = Path.GetFileName(path);
                string extension = Path.GetExtension(_name);
                if (extension.ToLower() == ".content")
                {
                    var fileInfo = new FileInfo(path);
                    IsHidden = (fileInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;

                    _xmlDoc = new XmlDocument();
                    _xmlDoc.Load(path);
                    ApplyConfiguredXslt(_xmlDoc);

                    XmlNode nameNode = _xmlDoc.SelectSingleNode("/ContentMetaData/ContentName");
                    _name = nameNode == null ? Path.GetFileNameWithoutExtension(_name) : nameNode.InnerText;

                    _contentTypeName = _xmlDoc.SelectSingleNode("/ContentMetaData/ContentType").InnerText;

                    ClearPermissions = _xmlDoc.SelectSingleNode("/ContentMetaData/Permissions/Clear") != null;
                    HasBreakPermissions = _xmlDoc.SelectSingleNode("/ContentMetaData/Permissions/Break") != null;
                    HasPermissions = _xmlDoc.SelectNodes("/ContentMetaData/Permissions/Identity").Count > 0;

                    foreach (XmlAttribute attachmentAttr in _xmlDoc.SelectNodes("/ContentMetaData/Fields/*/@attachment"))
                    {
                        string attachment = attachmentAttr.Value;
                        _attachments.Add(attachment);
                        bool isFolder = Directory.Exists(Path.Combine(directoryName, attachment));
                        if (isFolder)
                        {
                            if (_isFolder)
                                throw new ApplicationException(String.Concat("Two or more attachment folder is not enabled. ContentName: ", _name));
                            _isFolder = true;
                            _childrenFolder = Path.Combine(directoryName, attachment);
                        }
                    }
                    //-- default attachment
                    var defaultAttachmentPath = Path.Combine(directoryName, _name);
                    if (!_attachments.Contains(_name))
                    {
                        string[] paths;
                        if (Directory.Exists(defaultAttachmentPath))
                            paths = new string[] { defaultAttachmentPath };
                        else
                            paths = new string[0];

                        if (paths.Length == 1)
                        {
                            if (_isFolder)
                                throw new ApplicationException(String.Concat("Two or more attachment folder is not enabled. ContentName: ", _name));
                            _isFolder = true;
                            _childrenFolder = defaultAttachmentPath;
                            _attachments.Add(_name);
                        }
                        else
                        {
                            if (System.IO.File.Exists(defaultAttachmentPath))
                                _attachments.Add(_name);
                        }
                    }
                }
                else
                {
                    _isFolder = Directory.Exists(path);
                    if (_isFolder)
                    {
                        var dirInfo = new DirectoryInfo(path);
                        IsHidden = (dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;

                        _contentTypeName = "Folder";
                        _childrenFolder = path;
                    }
                    else
                    {
                        var fileInfo = new FileInfo(path);
                        IsHidden = (fileInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;

                        _xmlDoc = new XmlDocument();
                        _contentTypeName = GetContentTypeName(path) ?? "File";

                        var contentMetaData = String.Concat("<ContentMetaData><ContentType>{0}</ContentType><Fields><Binary attachment='", _name.Replace("'", "&apos;"), "' /></Fields></ContentMetaData>");
                        _xmlDoc.LoadXml(String.Format(contentMetaData, _contentTypeName));
                        _attachments.Add(_name);
                    }
                }
            }
            catch (Exception e)
            {
                throw new ApplicationException("Cannot create a ContentInfo. Path: " + path, e);
            }
        }

        public bool SetMetadata(Content content, string currentDirectory, bool isNewContent, bool needToValidate, bool updateReferences)
        {
            bool result = false;
            string cType = content["Type"]?.ToString();
            try
            {
                if (_xmlDoc != null)
                {
                    cType = _xmlDoc.SelectSingleNode("/ContentMetaData/ContentType")?.InnerText;
                    _transferringContext = new ImportContext(
                        _xmlDoc.SelectNodes("/ContentMetaData/Fields/*"), cType, currentDirectory, isNewContent, needToValidate, updateReferences);

                    result = ContentMetaData.SetFields(content, _transferringContext);
                }

                // only should save if newcontent OR field have changed
                if ((isNewContent && !Program._appConfig.FileTypes.Any(f => f == cType)) || result)
                {
                    try
                    {
                        content.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch (Exception ex) when (IsEmptyResponseSaveError(ex))
                    {
                        // Some servers return an empty body for successful save operations.
                        // In that case, try to reload by path and continue.
                        if (!TryReloadContentAfterEmptyResponse(content))
                            throw;

                        Log.Warning($"Save returned an empty response, continuing with reloaded content: {content.Path ?? _metaDataPath}");
                    }
                    catch (Exception ex) when (IsIgnorableFileSaveError(ex, cType))
                    {
                        Log.Warning($"Save failed for file metadata update, continuing import: {content.Path ?? _metaDataPath}. Reason: {ex.Message}");
                    }
                    catch (Exception ex) when (IsIgnorableUserPasswordError(ex, cType))
                    {
                        Log.Warning($"User save failed (Password field rejected by server), continuing import: {content.Path ?? _metaDataPath}. Server error: {ex.Message}");
                    }

                    Thread.Sleep(100);
                    _contentId = content.Id;

                    if (content.Id == 0)
                    {
                        Log.Error($"Something went wrong! {content?.Name}");
                    }
                }

                return result;
            }
            catch (Exception ex) when (IsIgnorableFileSaveError(ex, cType))
            {
                Log.Warning($"Metadata processing failed for file content, continuing import: {content.Path ?? _metaDataPath}. Reason: {ex.Message}");
                return false;
            }
            catch (Exception ex) when (IsIgnorableUserPasswordError(ex, cType))
            {
                Log.Warning($"User save failed (Password field rejected by server), continuing import: {content.Path ?? _metaDataPath}. Server error: {ex.Message}");
                return false;
            }
        }

        private static bool IsEmptyResponseSaveError(Exception ex)
        {
            while (ex != null)
            {
                if (!string.IsNullOrWhiteSpace(ex.Message) && ex.Message.IndexOf("No object can be recognized from the response if it is empty", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                ex = ex.InnerException;
            }

            return false;
        }

        private static bool TryReloadContentAfterEmptyResponse(Content content)
        {
            var repository = content?.Repository;
            if (repository == null)
                return false;

            var fallbackPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(content.Path))
                fallbackPaths.Add(content.Path);
            if (!string.IsNullOrWhiteSpace(content.ParentPath) && !string.IsNullOrWhiteSpace(content.Name))
                fallbackPaths.Add(RepositoryPath.Combine(content.ParentPath, content.Name));

            if (fallbackPaths.Count == 0)
                return false;

            const int maxAttempts = 5;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                foreach (var path in fallbackPaths)
                {
                    try
                    {
                        var reloaded = repository.LoadContentAsync(path, CancellationToken.None).GetAwaiter().GetResult();
                        if (reloaded == null)
                            continue;

                        content.Id = reloaded.Id;
                        content.Path = reloaded.Path;
                        content.Name = reloaded.Name;
                        return true;
                    }
                    catch
                    {
                        // Retry below: some servers need a short delay before the new/updated content becomes available.
                    }
                }

                if (attempt < maxAttempts - 1)
                    Thread.Sleep(200);
            }

            return false;
        }

        private static void ApplyConfiguredXslt(XmlDocument xml)
        {
            if (xml == null)
                return;

            var xslt = GetConfiguredXslt();
            if (xslt == null)
            {
                Log.Debug("No XSLT configured, skipping transform.");
                return;
            }

            var beforeFields = xml.SelectSingleNode("/ContentMetaData/Fields")?.InnerXml;
            Log.Debug($"XSLT transform BEFORE: {beforeFields?.Substring(0, Math.Min(beforeFields?.Length ?? 0, 500))}");

            using var sw = new StringWriter();
            using (var xw = XmlWriter.Create(sw, xslt.OutputSettings))
            {
                using var sr = new StringReader(xml.OuterXml);
                using var xr = XmlReader.Create(sr);
                xslt.Transform(xr, null, xw);
            }

            xml.LoadXml(sw.ToString());

            var afterFields = xml.SelectSingleNode("/ContentMetaData/Fields")?.InnerXml;
            Log.Debug($"XSLT transform AFTER:  {afterFields?.Substring(0, Math.Min(afterFields?.Length ?? 0, 500))}");
        }

        private static XslCompiledTransform GetConfiguredXslt()
        {
            if (_importXsltInitialized)
                return _importXslt;

            lock (_xsltLock)
            {
                if (_importXsltInitialized)
                    return _importXslt;

                var configuredPath = Program._appConfig?.XsltPath;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    _importXslt = null;
                    _importXsltInitialized = true;
                    return null;
                }

                var resolvedPath = ResolveXsltPath(configuredPath);

                if (!System.IO.File.Exists(resolvedPath))
                    throw new FileNotFoundException($"Configured XSLT file was not found: {resolvedPath}");

                var transform = new XslCompiledTransform();
                transform.Load(resolvedPath, XsltSettings.Default, new XmlUrlResolver());
                _importXslt = transform;
                _importXsltInitialized = true;

                Log.Information($"Import XSLT loaded: {resolvedPath}");
                return _importXslt;
            }
        }

        private static string ResolveXsltPath(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
                return configuredPath;

            var candidates = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), configuredPath),
                Path.Combine(AppContext.BaseDirectory, configuredPath),
                Path.Combine(Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? string.Empty, configuredPath)
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            foreach (var candidate in candidates)
            {
                if (System.IO.File.Exists(candidate))
                    return candidate;
            }

            return candidates.FirstOrDefault() ?? configuredPath;
        }

        private static bool IsIgnorableFileSaveError(Exception ex, string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return false;

            var isFileLike = Program._appConfig.FileTypes?.Any(ft => string.Equals(ft, contentType, StringComparison.OrdinalIgnoreCase)) == true;
            if (!isFileLike)
                return false;

            while (ex != null)
            {
                if (!string.IsNullOrWhiteSpace(ex.Message) && ex.Message.IndexOf("Object reference not set to an instance of an object", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                ex = ex.InnerException;
            }

            return false;
        }

        private static bool IsIgnorableUserPasswordError(Exception ex, string contentType)
        {
            if (!string.Equals(contentType, "User", StringComparison.OrdinalIgnoreCase))
                return false;

            while (ex != null)
            {
                if (!string.IsNullOrWhiteSpace(ex.Message) && ex.Message.IndexOf("Invalid Fields: Password", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                ex = ex.InnerException;
            }

            return false;
        }

        public bool UpdateReferences(Content content, bool needToValidate)
        {
            // update reference fields only
            if (_transferringContext == null)
                _transferringContext = new ImportContext(_xmlDoc.SelectNodes("/ContentMetaData/Fields/*"), _xmlDoc.SelectSingleNode("/ContentMetaData/ContentType")?.InnerText, null, false, needToValidate, true);
            else
                _transferringContext.UpdateReferences = true;
            if (!ContentMetaData.SetFields(content, _transferringContext))
                return false;

            // exit if no permission settings available
            if (!HasPermissions && !HasBreakPermissions)
                return true;

            // set permissions
            var permissionsNode = _xmlDoc.SelectSingleNode("/ContentMetaData/Permissions");
            // here should import Permissions(permissionsNode, this._metaDataPath);
            ImportPermissions(content, permissionsNode /*, this._metaDataPath*/);

            return true;
        }

        private bool ImportPermissions(Content content, XmlNode permissionsNode /*, string metaDataPath*/)
        {
            bool result = false;
            string metadataPath = "";
            // parsing and executing 'Break' and 'Clear'
            var breakNode = permissionsNode.SelectSingleNode("Break");
            var clearNode = permissionsNode.SelectSingleNode("Clear");
            if (breakNode != null)
            {
                content.BreakInheritanceAsync().GetAwaiter().GetResult();
            }
            else
            {
                content.UnbreakInheritanceAsync().GetAwaiter().GetResult();
            }
            // executing 'Clear'
            if (clearNode != null)
            {
                // TODO: how can RemoveExplicitEntries?
            }

            var identityElementIndex = 0;
            List<SetPermissionRequest> sprList = new List<SetPermissionRequest>();

            var identities = permissionsNode.SelectNodes("Identity");
            if (identities.Count > 0)
            {
                foreach (XmlElement identityElement in identities)
                {
                    identityElementIndex++;

                    // checking identity path
                    var path = identityElement.GetAttribute("path");
                    var propagationAttr = identityElement.GetAttribute("propagation");
                    var localOnly = propagationAttr == null ? false : propagationAttr.ToLowerInvariant() == "localonly";
                    if (String.IsNullOrEmpty(path))
                    {
                        Log.Error($"Missing or empty path attribute of the Identity element {identityElementIndex} so skipped. {metadataPath}");
                        //Thread.Sleep(1000);
                        continue;
                    }
                    //var pathCheck = Content.ExistsAsync(path).GetAwaiter().GetResult();
                    //if (pathCheck != RepositoryPath.PathResult.Correct)
                    //    throw ImportPermissionExceptionHelper(String.Concat("Invalid path of the Identity element ", identityElementIndex, ": ", path, " (", pathCheck, ")."), metadataPath, null);

                    // getting identity node
                    var repository = content.Repository ?? throw new InvalidOperationException("Repository access is not available during permission import.");
                    var identityNode = repository.LoadContentAsync(path, CancellationToken.None).GetAwaiter().GetResult();
                    if (identityNode == null)
                    {
                        //throw new Exception($"Identity {path} was not found: {path}. {metadataPath}");
                        Log.Error($"Identity {identityElementIndex} was not found and so skipped: {path}. {metadataPath}");
                        //Thread.Sleep(1000);
                        continue;
                    }

                    var spr = new SetPermissionRequest();
                    spr.Identity = identityNode.Path;
                    spr.LocalOnly = localOnly;

                    // parsing value array
                    foreach (XmlElement permissionElement in identityElement.SelectNodes("*"))
                    {
                        var permName = permissionElement.LocalName;

                        switch (permissionElement.InnerText.ToLower())
                        {
                            case "allow":
                            case "allowed":
                                foreach (FieldInfo fieldInfo in spr.GetType().GetFields())
                                {
                                    if (permName == fieldInfo.Name)
                                    {
                                        fieldInfo.SetValue(spr, PermissionValue.Allow);
                                    }
                                }
                                break;
                            case "deny":
                            case "denied":
                                foreach (FieldInfo fieldInfo in spr.GetType().GetFields())
                                {
                                    if (permName == fieldInfo.Name)
                                    {
                                        fieldInfo.SetValue(spr, PermissionValue.Deny);
                                    }
                                }
                                break;
                            default:
                                throw new Exception($"Invalid permission value in Identity {identityElementIndex}: {permissionElement.InnerText}. Allowed values: Allowed, Denied, {metadataPath}");
                        }
                    }
                    sprList.Add(spr);
                }
                SecurityManager.SetPermissionsAsync(content.Id, sprList.ToArray()).GetAwaiter().GetResult();
                result = true;
            }

            return result;
        }

        private static string GetContentTypeName(string fileName)
        {
            if (Configuration == null)
                return null;

            int extStart = fileName.LastIndexOf('.');
            if (extStart != -1)
            {
                var extension = fileName.Substring(extStart).Replace(".", "");

                if (!string.IsNullOrEmpty(extension))
                {
                    var fileType = Configuration.GetSection($"sensenet:UploadFileExtensions:{extension}").Value;
                    if (!string.IsNullOrEmpty(fileType))
                    {
                        return fileType;
                    }
                }
            }

            return null;
        }
    }
}
