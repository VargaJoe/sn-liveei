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
            if (_xmlDoc == null)
                return true;
            _transferringContext = new ImportContext(
                _xmlDoc.SelectNodes("/ContentMetaData/Fields/*"), _xmlDoc.SelectSingleNode("/ContentMetaData/ContentType")?.InnerText, currentDirectory, isNewContent, needToValidate, updateReferences);

            bool result = ContentMetaData.SetFields(content, _transferringContext);
            _contentId = content.Id;
            return result;
        }

        public bool UpdateReferences(Content content, bool needToValidate)
        {
            if (_transferringContext == null)
                _transferringContext = new ImportContext(_xmlDoc.SelectNodes("/ContentMetaData/Fields/*"), _xmlDoc.SelectSingleNode("/ContentMetaData/ContentType")?.InnerText, null, false, needToValidate, true);
            else
                _transferringContext.UpdateReferences = true;

            if (!ContentMetaData.SetFields(content, _transferringContext))
                return false;
            if (!HasPermissions && !HasBreakPermissions)
                return true;
            
            //var permissionsNode = _xmlDoc.SelectSingleNode("/ContentMetaData/Permissions");
            // here should import Permissions(permissionsNode, this._metaDataPath);

            return true;
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
