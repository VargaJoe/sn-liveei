using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using SenseNet.Client;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;

namespace SnLiveExportImport
{
    public static class LiveExport
    {
        public static List<JObject> ContentTypes { get; set; }
        public static List<Content> ContentTypeContents { get; set; }
        public static List<JObject> ContentFields { get; set; }

        public static void StartExport()
        {
            // prepare ctd info
            ContentTypes = GetCtds();

            ContentTypeContents = Content.QueryForAdminAsync("Type:ContentType").GetAwaiter().GetResult().ToList();

            // prepare field info
            ContentFields = GetFields(ContentTypes);

            // TODO: get variables from parameters and/or settings
            int lastSlash = Program._appConfig.RepoPath.LastIndexOf("/");
            string sourceRepoParentPath = Program._appConfig.RepoPath.Substring(0, lastSlash);
            string sourceRepoPath = Program._appConfig.RepoPath;
            string targetBasePath = Program._appConfig.LocalPath;

            string queryPath = string.Empty;
            bool all = Program._appConfig.TreeExport;
            bool syncmode = Program._appConfig.SyncMode;

            //string combino = Path.Combine(targetBasePath, fsTargetRepoPath);
            //string cbPath = (syncmode) ? targetBasePath : $"{targetBasePath}{DateTime.Now.Ticks}";
            string fsSourceRepoParentPath = $".{sourceRepoParentPath}";
            string cbPath = (syncmode) ? Path.Combine(targetBasePath, fsSourceRepoParentPath) : $"{targetBasePath}-{DateTime.Now.ToString("yyyyMMdd-HHmmss")}";
            string fsPath = Path.GetFullPath(cbPath);

            ExportContents(sourceRepoPath, fsPath, all);
        }

        public static void ExportContents(string repositoryPath, string fsPath, bool all)
        {
            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(fsPath);
                if (!dirInfo.Exists)
                    Directory.CreateDirectory(fsPath);

                Log.Information($"Target directory exists: {fsPath}. Exported contents will override existing subelements.");

                var server = ClientContext.Current.Server;
                var lastSlashPos = repositoryPath.LastIndexOf("/");
                var parentPath = repositoryPath.Substring(0, lastSlashPos);
                var contentName = repositoryPath.Substring(lastSlashPos + 1);

                //-- load export root
                //var root = Content.LoadAsync(repositoryPath).GetAwaiter().GetResult();
                JObject root = LoadAsyncJObject($"{server.Url}/odata.svc{parentPath}/('{contentName}')?metadata=no", ClientContext.Current.Server);

                if (root != null)
                {
                    Log.Information($"=========================== Export ===========================");
                    Log.Information($"From: {repositoryPath}");
                    Log.Information($"To:   {fsPath}");
                    Log.Information($"==============================================================");

                    var context = new ExportContext(repositoryPath, fsPath);

                    var fullPath = $"{fsPath}"; //Path.Combine(fsPath, root.Path.TrimStart('/').Replace('/', '\\'));
                    //var fullDirPath = Path.GetDirectoryName(fullPath);
                    var fullDirPath = fullPath;


                    if (!Directory.Exists(fullDirPath))
                        Directory.CreateDirectory(fullDirPath);

                    if (all)
                        ExportContentTree(root, context, fullDirPath, "");
                    else
                        ExportContent(root, context, fullDirPath, "");

                    Log.Information($"--------------------------------------------------------------");
                    Log.Information($"Outer references:");

                    //var outerRefs = context.GetOuterReferences();

                    ////Log
                    //if (outerRefs.Count == 0)
                    //    Log.Information($"All references are exported.");
                    ////else
                    ////    foreach (var item in outerRefs)
                    ////        Log.Information(item);
                    ////Log
                }
                else
                {
                    Log.Information($"Content does not exist: ", repositoryPath);
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.Write(e.Message);
                Log.Error($"{e}, {fsPath}");
            }


            Log.Information($"==============================================================");
            //if (exceptions == 0)
            //    Log.Information($"Export is successfully finished.");
            //else
            //    Log.Information($"Export is finished with ", exceptions, " errors.");
        }

        private static void ExportContentTree(JObject content, ExportContext context, string fsPath, string indent)
        {
            ExportContent(content, context, fsPath, indent);

            var contentType = content["Type"]?.ToString();
            var contentName = content["Name"]?.ToString();

            if (string.IsNullOrWhiteSpace(contentName))
                return;

            if (contentType == "SmartFolder")
                return;

            // don't walk under file types
            if (Program._appConfig.FileTypes.Any(f => f == contentType))
                return;

            // TODO: should skip any non-folder types

            var server = ClientContext.Current.Server;
            var contentPath = content["Path"]?.ToString();

            // walk through only if it has children
            JObject contentAsFolder = LoadContainerAsyncJObject($"{server.Url}/odata.svc{contentPath}?metadata=no", ClientContext.Current.Server);
            if (contentAsFolder == null)
                return;

            int childCount = 0;

            //var queryResult = contentAsFolder.GetChildren(new QuerySettings { EnableAutofilters = false, EnableLifespanFilter = false });
            //var childCount = queryResult.Count;

            if (!int.TryParse(contentAsFolder["__count"]?.ToString(), out childCount) || childCount == 0)
                return;

            var children = contentAsFolder["results"] as JArray;
            var newDir = Path.Combine(fsPath, contentName);

            if (contentType != "ContentType")
                Directory.CreateDirectory(newDir);

            var newIndent = indent + "  ";
            foreach (JObject childContent in children)
            {
                ExportContentTree(childContent, context, newDir, newIndent);
            }
        }

        private static void ExportContentTree(Content content, ExportContext context, string fsPath, string indent)
        {
            ExportContent(content, context, fsPath, indent);

            var contentType = content["Type"]?.ToString();
            var contentName = content["Name"]?.ToString();

            if (string.IsNullOrWhiteSpace(contentName))
                return;

            if (contentType == "SmartFolder")
                return;

            // don't walk under file types
            if (Program._appConfig.FileTypes.Any(f => f == contentType))
                return;

            // TODO: should skip any non-folder types

            var server = ClientContext.Current.Server;
            var contentPath = content["Path"]?.ToString();

            // walk through only if it has children
            //JObject contentAsFolder = LoadContainerAsyncJObject($"{server.Url}/odata.svc{contentPath}?metadata=no", ClientContext.Current.Server);

            var children = Content.LoadCollectionAsync(contentPath).GetAwaiter().GetResult();

            if (children == null)
                return;

            int childCount = children.Count();

            //var queryResult = contentAsFolder.GetChildren(new QuerySettings { EnableAutofilters = false, EnableLifespanFilter = false });
            //var childCount = queryResult.Count;

            if (childCount == 0)
                return;
            
            var newDir = Path.Combine(fsPath, contentName);

            if (contentType != "ContentType")
                Directory.CreateDirectory(newDir);

            var newIndent = indent + "  ";
            foreach (Content childContent in children)
            {
                ExportContentTree(childContent, context, newDir, newIndent);
            }
        }

        private static void ExportContent(Content content, ExportContext context, string fsPath, string indent)
        {
            context.CurrentDirectory = fsPath;
            var contentPath = content?["Path"]?.ToString();
            var contentName = content?["Name"]?.ToString();
            var contentType = content["Type"]?.ToString();

            if (contentType == "ContentType")
            {
                Log.Information($"{contentPath} (TODO)");

                ExportContentType(content.Name, content["Type"]?.ToString(), context);
                return;
            }

            Log.Information(contentPath);

            string metaFilePath = Path.Combine(fsPath, contentName + ".Content");
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = Encoding.UTF8;
            settings.Indent = true;
            settings.IndentChars = "  ";

            using (XmlWriter writer = XmlWriter.Create(metaFilePath, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("ContentMetaData");
                writer.WriteElementString("ContentType", content["Type"]?.ToString());
                writer.WriteElementString("ContentName", contentName);
                writer.WriteStartElement("Fields");
                try
                {
                    ExportFieldData(content, writer, context);
                }
                catch (Exception e)
                {
                    Log.Error($"{e.Message}{((e.InnerException != null) ? ", " + e.InnerException.Message : string.Empty)}");
                }
                writer.WriteEndElement();
                writer.WriteStartElement("Permissions");
                writer.WriteElementString("Clear", null);
                ExportPermissions(contentPath, writer);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        private static void ExportContent(JObject content, ExportContext context, string fsPath, string indent)
        {
            context.CurrentDirectory = fsPath;
            var contentPath = content?["Path"]?.ToString();
            var contentName = content?["Name"]?.ToString();
            var contentType = content["Type"]?.ToString();

            if (contentType == "ContentType")
            {
                Log.Information($"{contentPath} (TODO)");

                ExportContentType(content["Name"]?.ToString(), content["Type"]?.ToString(), context);
                return;
            }

            Log.Information(contentPath);

            string metaFilePath = Path.Combine(fsPath, contentName + ".Content");
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = Encoding.UTF8;
            settings.Indent = true;
            settings.IndentChars = "  ";

            using (XmlWriter writer = XmlWriter.Create(metaFilePath, settings))
            {
                //<?xml version="1.0" encoding="utf-8"?>
                //<ContentMetaData>
                //    <ContentType>Site</ContentType>
                //    <Fields>
                //        ...
                writer.WriteStartDocument();
                writer.WriteStartElement("ContentMetaData");
                writer.WriteElementString("ContentType", content["Type"]?.ToString());
                writer.WriteElementString("ContentName", contentName);
                writer.WriteStartElement("Fields");
                try
                {
                    ExportFieldData(content, writer, context);
                }
                catch (Exception e)
                {
                    Log.Error($"{e.Message}{((e.InnerException != null)?", "+e.InnerException.Message:string.Empty)}");
                }
                writer.WriteEndElement();
                writer.WriteStartElement("Permissions");
                writer.WriteElementString("Clear", null);
                ExportPermissions(contentPath, writer);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        public static void ExportFieldData(Content content, XmlWriter writer, ExportContext context)
        {
            var server = ClientContext.Current.Server;
            var baseUrl = server.Url;

            // first of all: exporting aspect names
            if (content != null)
            {
                JArray aspects = content["Aspects"] as JArray;
                if (aspects != null && aspects.Count > 0)
                    writer.WriteElementString("Aspects", String.Join(", ", aspects.Select(j => j.ToString())));
            }
            var contentName = content["Name"]?.ToString();
            var contentType = content["Type"]?.ToString();
            var typeContent = ContentTypes.FirstOrDefault(c => c["ContentTypeName"]?.ToString() == contentType);

            // exit if content type is "ContentType" 
            if (contentType == "ContentType")
                return;

            var contentId = 0;
            if (!int.TryParse(content["Id"]?.ToString(), out contentId) || contentId == 0)
                return;

            // exporting other fields
            //foreach (JObject field in typeContent["FieldSettings"])
            //{
            //    var fieldType = field["Type"]?.ToString().Replace("FieldSetting", "");
            //    if (string.IsNullOrWhiteSpace(fieldType))
            //        continue;

            //    var fieldName = field["Name"]?.ToString();
            //    if (excludeFields.Any(f => f == fieldName))
            //        continue;

            //    var fieldValue = content[fieldName];

            // schemas from getschema have only local field definitions
            //}

            var contentTypeFromSchema = ContentTypes.FirstOrDefault(ct => ct["ContentTypeName"]?.ToString() == contentType);
            var contentTypeFields = contentTypeFromSchema["FieldSettings"];

            foreach (JObject contentField in contentTypeFields)
            {
                var fieldName = contentField["Name"]?.ToString();

                if (!Program._appConfig.ExcludedExportFields.Any(f => f == fieldName))
                {
                    if (contentField == null)
                        continue;

                    if (bool.TryParse(contentField["ReadOnly"]?.ToString(), out bool readOnly) && readOnly)
                        continue;

                    var fieldType = contentField["Type"]?.ToString().Replace("FieldSetting", "");
                    var fieldValue = content[fieldName];
                    var fieldValueType = fieldValue.GetType().Name;

                    if (string.IsNullOrWhiteSpace(fieldType))
                        continue;

                    if (((JToken)fieldValue).Type == JTokenType.Null)
                        continue;

                    //if (!HasExportData)
                    //    return;

                    //FieldSubType subType;
                    //var exportName = GetExportName(this.Name, out subType);

                    //writer.WriteStartElement(fieldName);
                    //if (subType != FieldSubType.General)
                    //    writer.WriteAttributeString(FIELDSUBTYPEATTRIBUTENAME, subType.ToString());

                    //ExportData(writer, context);
                    //var d = ((JObject)content[field])["__deferred"]["uri"];
                    switch (fieldType)
                    {
                        case "DateTime":
                            DateTime workaroundDate = DateTime.MinValue;
                            if (DateTime.TryParse(fieldValue?.ToString(), out workaroundDate))
                            {
                                writer.WriteStartElement(fieldName);
                                writer.WriteString(workaroundDate.ToString("yyyy-MM-ddTHH:mm:ss"));
                                writer.WriteEndElement();
                            }
                            break;
                        case "Reference":
                            var deferredUri = ((JObject)fieldValue)["__deferred"]?["uri"]?.ToString();
                            
                            if (string.IsNullOrWhiteSpace(deferredUri))
                                continue;

                            object refResponse = null;
                            RESTCaller.ProcessWebResponseAsync($"{baseUrl}{deferredUri}", System.Net.Http.HttpMethod.Get, server, async response =>
                            {
                                if (response == null)
                                    return;
                                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                using (var reader = new StreamReader(stream))
                                {
                                    var textResponse = reader.ReadToEnd();
                                    refResponse = Newtonsoft.Json.JsonConvert.DeserializeObject(textResponse);
                                }
                            }, CancellationToken.None).GetAwaiter().GetResult();

                            if (refResponse != null)
                            {
                                writer.WriteStartElement(fieldName);
                                if (((JObject)refResponse)["d"]["__count"] != null)
                                {
                                    // multiple references
                                    var refResults = ((JObject)refResponse)["d"]["results"] as JArray;
                                    if (fieldType == "Reference")
                                    {
                                        foreach (var refField in refResults)
                                        {
                                            writer.WriteStartElement("Path");
                                            writer.WriteString((refField as JObject)?["Path"]?.ToString());
                                            writer.WriteEndElement();
                                        }
                                    }
                                    else if (fieldType == "AllowedChildTypes")
                                    {
                                        writer.WriteString(string.Join(" ", (refResults as JArray).Select(f => ((JObject)f)?["Name"]?.ToString())));
                                    }
                                }
                                else
                                {
                                    // only single reference
                                    if (fieldType == "Reference")
                                    {
                                        writer.WriteStartElement("Path");
                                        writer.WriteString((refResponse as JObject)?["d"]?["Path"]?.ToString());
                                        writer.WriteEndElement();
                                    }
                                    else if (fieldType == "AllowedChildTypes")
                                    {
                                        writer.WriteString((refResponse as JObject)?["d"]?["Name"]?.ToString());
                                    }
                                }
                                writer.WriteEndElement();
                            }
                            break;
                        case "Boolean":
                        case "ShortText":
                        case "LongText":
                        case "Integer":
                        case "Number":
                        case "Null":
                            writer.WriteStartElement(fieldName);
                            writer.WriteString(fieldValue?.ToString());
                            writer.WriteEndElement();
                            break;
                        case "Choice":
                            writer.WriteStartElement(fieldName);
                            writer.WriteString(string.Join(',', fieldValue));
                            writer.WriteEndElement();
                            break;
                        case "Binary":
                            var binUrl = ((JObject)fieldValue)["__mediaresource"]?["media_src"]?.ToString();
                            if (string.IsNullOrWhiteSpace(binUrl))
                                continue;

                            RESTCaller.ProcessWebResponseAsync($"{baseUrl}{binUrl}", System.Net.Http.HttpMethod.Get, server, async response =>
                            {
                                if (response == null)
                                    return;

                                var fsDirectory = context.CurrentDirectory;
                                var fileName = contentName;
                                byte[] fileBytes;

                                using (var sourceStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                {
                                    using (var helperStream = new MemoryStream())
                                    {
                                        sourceStream.CopyTo(helperStream);
                                        fileBytes = helperStream.ToArray();
                                    }
                                }

                                // extension workaround
                                if (fieldName != "Binary")
                                {
                                    var mime = "";// fieldValue["__mediaresource"]?["content_type"]?.ToString();
                                    var ext = GetDefaultExtension(mime);

                                    var magic = GetMime(fileBytes.Take(256).ToArray());
                                    ext = GetDefaultExtension(magic);

                                    if (string.IsNullOrWhiteSpace(ext) && fieldName == "ImageData")
                                        ext = "jpg";

                                    fileName += "." + fieldName + ext;
                                }
                                var fsPath = Path.Combine(fsDirectory, fileName);

                                writer.WriteStartElement(fieldName);
                                writer.WriteAttributeString("attachment", fileName);

                                using (var targetFile = new FileStream(fsPath, FileMode.Create))
                                {
                                    for (var i = 0; i < fileBytes.Length; i++)
                                        targetFile.WriteByte((byte)fileBytes[i]);
                                }

                                writer.WriteEndElement();
                            }, CancellationToken.None).GetAwaiter().GetResult();
                            break;
                        default:
                            Log.Warning($"NOT IMPLEMENTED: {fieldType}");
                            break;
                    }
                }
            }
        }

        public static void ExportFieldData(JObject content, XmlWriter writer, ExportContext context)
        {
            var server = ClientContext.Current.Server;
            var baseUrl = server.Url;

            // first of all: exporting aspect names
            if (content != null)
            {
                JArray aspects = content["Aspects"] as JArray;
                if (aspects != null && aspects.Count > 0)
                    writer.WriteElementString("Aspects", String.Join(", ", aspects.Select(j => j.ToString())));
            }
            var contentName = content["Name"]?.ToString();
            var contentType = content["Type"]?.ToString();
            var typeContent = ContentTypes.FirstOrDefault(c => c["ContentTypeName"]?.ToString() == contentType);

            // exit if content type is "ContentType" 
            if (contentType == "ContentType")
                return;

            var contentId = 0;
            if (!int.TryParse(content["Id"]?.ToString(), out contentId) || contentId == 0)
                return;

            // exporting other fields
            //foreach (JObject field in typeContent["FieldSettings"])
            //{
            //    var fieldType = field["Type"]?.ToString().Replace("FieldSetting", "");
            //    if (string.IsNullOrWhiteSpace(fieldType))
            //        continue;

            //    var fieldName = field["Name"]?.ToString();
            //    if (excludeFields.Any(f => f == fieldName))
            //        continue;

            //    var fieldValue = content[fieldName];

            // schemas from getschema have only local field definitions
            //}
            
            var contentTypeFromSchema = ContentTypes.FirstOrDefault(ct => ct["ContentTypeName"]?.ToString() == contentType);

            foreach (JProperty field in content.Properties())
            {
                var fieldName = field.Name;
                var fieldValue = field.Value;

                if (!Program._appConfig.ExcludedExportFields.Any(f => f == fieldName))
                {
                    // not all fields can be fount on given contenttype
                    var contentField = ContentFields.FirstOrDefault(cf => cf["Name"]?.ToString() == fieldName);
                    var contentFieldOwn = contentTypeFromSchema?["FieldSettings"].FirstOrDefault(cf => cf["Name"]?.ToString() == fieldName);

                    if (contentField == null)
                        continue;
                    
                    // readonly field varies by content types
                    if (contentFieldOwn != null && bool.TryParse(contentFieldOwn["ReadOnly"]?.ToString(), out bool readOnly) && readOnly)
                        continue;

                    //var fieldType = contentField["Type"]?.ToString().Replace("FieldSetting", "");
                    var fieldClassType = contentField["FieldClassName"]?.ToString();
                    var lastPointPos = fieldClassType.LastIndexOf(".");
                    var lastFieldPos = fieldClassType.LastIndexOf("Field");
                    var fieldType = fieldClassType.Substring(lastPointPos + 1).Substring(0, lastFieldPos - lastPointPos - 1);
                    if (string.IsNullOrWhiteSpace(fieldType))
                        continue;

                    if (fieldValue.Type == JTokenType.Null)
                        continue;


                    //if (!HasExportData)
                    //    return;

                    //FieldSubType subType;
                    //var exportName = GetExportName(this.Name, out subType);

                    //writer.WriteStartElement(fieldName);
                    //if (subType != FieldSubType.General)
                    //    writer.WriteAttributeString(FIELDSUBTYPEATTRIBUTENAME, subType.ToString());

                    //ExportData(writer, context);
                    //var d = ((JObject)content[field])["__deferred"]["uri"];
                    switch (fieldType)
                    {
                        case "DateTime":
                            DateTime workaroundDate = DateTime.MinValue;
                            if (fieldValue.Type == JTokenType.Date && DateTime.TryParse(fieldValue?.ToString(), out workaroundDate))
                            {
                                writer.WriteStartElement(fieldName);
                                writer.WriteString(workaroundDate.ToString("yyyy-MM-ddTHH:mm:ss"));
                                writer.WriteEndElement();
                            }
                            break;
                        case "AllowedChildTypes":
                        case "Reference":
                            if (fieldValue["__deferred"] == null)
                                continue;

                            var deferredUri = fieldValue["__deferred"]["uri"]?.ToString();

                            object refResponse = null;
                            RESTCaller.ProcessWebResponseAsync($"{baseUrl}{deferredUri}", System.Net.Http.HttpMethod.Get, server, async response =>
                            {
                                if (response == null)
                                    return;
                                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                using (var reader = new StreamReader(stream))
                                {
                                    var textResponse = reader.ReadToEnd();
                                    refResponse = Newtonsoft.Json.JsonConvert.DeserializeObject(textResponse);
                                }
                            }, CancellationToken.None).GetAwaiter().GetResult();

                            if (refResponse != null)
                            {
                                writer.WriteStartElement(fieldName);
                                if (((JObject)refResponse)["d"]["__count"] != null)
                                {
                                    // multiple references
                                    var refResults = ((JObject)refResponse)["d"]["results"] as JArray;
                                    if (fieldType == "Reference")
                                    {
                                        foreach (var refField in refResults)
                                        {
                                            writer.WriteStartElement("Path");
                                            writer.WriteString((refField as JObject)?["Path"]?.ToString());
                                            writer.WriteEndElement();
                                        }
                                    }
                                    else if (fieldType == "AllowedChildTypes")
                                    {
                                        writer.WriteString(string.Join(" ", (refResults as JArray).Select(f => ((JObject)f)?["Name"]?.ToString())));
                                    }
                                }
                                else
                                {
                                    // only single reference
                                    if (fieldType == "Reference")
                                    {
                                        writer.WriteStartElement("Path");
                                        writer.WriteString((refResponse as JObject)?["d"]?["Path"]?.ToString());
                                        writer.WriteEndElement();
                                    }
                                    else if (fieldType == "AllowedChildTypes")
                                    {
                                        writer.WriteString((refResponse as JObject)?["d"]?["Name"]?.ToString());
                                    }
                                }
                                writer.WriteEndElement();
                            }
                            break;
                        case "Boolean":
                        case "ShortText":
                        case "LongText":
                        case "Integer":
                        case "Number":
                        case "Null":
                            writer.WriteStartElement(fieldName);
                            writer.WriteString(fieldValue?.ToString());
                            writer.WriteEndElement();
                            break;
                        case "Choice":
                            writer.WriteStartElement(fieldName);
                            writer.WriteString(string.Join(',', fieldValue));
                            writer.WriteEndElement();
                            break;
                        case "Binary":
                            var binUrl = fieldValue["__mediaresource"]?["media_src"]?.ToString();
                            if (string.IsNullOrWhiteSpace(binUrl))
                                continue;

                            RESTCaller.ProcessWebResponseAsync($"{baseUrl}{binUrl}", System.Net.Http.HttpMethod.Get, server, async response =>
                                {
                                    if (response == null)
                                        return;

                                    var fsDirectory = context.CurrentDirectory;
                                    var fileName = contentName;
                                    byte[] fileBytes;

                                    using (var sourceStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                    {
                                        using (var helperStream = new MemoryStream())
                                        {
                                            sourceStream.CopyTo(helperStream);
                                            fileBytes = helperStream.ToArray();
                                        }
                                    }

                                    // extension workaround
                                    if (fieldName != "Binary")
                                    {
                                        var mime = fieldValue["__mediaresource"]?["content_type"]?.ToString();
                                        var ext = GetDefaultExtension(mime);

                                        var magic = GetMime(fileBytes.Take(256).ToArray());
                                        ext = GetDefaultExtension(magic);

                                        if (string.IsNullOrWhiteSpace(ext) && fieldName == "ImageData")
                                            ext = "jpg";

                                        fileName += "." + fieldName + ext;
                                    }
                                    var fsPath = Path.Combine(fsDirectory, fileName);

                                    writer.WriteStartElement(fieldName);
                                    writer.WriteAttributeString("attachment", fileName);

                                    using (var targetFile = new FileStream(fsPath, FileMode.Create))
                                    {
                                        for (var i = 0; i < fileBytes.Length; i++)
                                            targetFile.WriteByte((byte)fileBytes[i]);
                                    }

                                    writer.WriteEndElement();
                                }, CancellationToken.None).GetAwaiter().GetResult();
                            break;
                        default:
                            Log.Warning($"NOT IMPLEMENTED: {fieldType}");
                            break;
                    }
                }
            }
        }

        public static void ExportPermissions(string contentPath, XmlWriter writer)
        {
            var permissionsRequest = new ODataRequest() { Path = contentPath, ActionName = "GetAcl", Metadata = MetadataFormat.None };
            var permissionsResult = RESTCaller.GetResponseStringAsync(permissionsRequest).GetAwaiter().GetResult();
            JObject acl = Newtonsoft.Json.JsonConvert.DeserializeObject(permissionsResult) as JObject;

            bool isInherits = false;
            if (bool.TryParse(acl["inherits"]?.ToString(), out isInherits) && !isInherits)
                writer.WriteElementString("Break", null);
            var entries = acl["entries"] as JArray;
            foreach (JObject aceInfo in entries)
                ExportPermission(aceInfo, writer);
        }

        public static void ExportPermission(JObject aceInfo, XmlWriter writer)
        {
            bool isInherited = false;
            if (bool.TryParse(aceInfo["inherited"]?.ToString(), out isInherited) && !isInherited)
            {
                writer.WriteStartElement("Identity");
                writer.WriteAttributeString("path", aceInfo["identity"]?["path"]?.ToString());
                //if (aceInfo.LocalOnly)
                //    writer.WriteAttributeString("propagation", "LocalOnly");
                var permissions = aceInfo["permissions"] as JObject;
                foreach (var permission in permissions.Properties())
                {
                    var permTypeName = permission.Name;
                    var permTypeValueObj = permission.Value as JObject;
                    if (permTypeValueObj != null) {
                        var permTypeValue = permTypeValueObj["value"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(permTypeValue))
                            writer.WriteElementString(permTypeName, permTypeValue);
                    }
                }
                writer.WriteEndElement();
            }
        }

        private static void ExportContentType(Content content, ExportContext context, string indent)
        {
            //BinaryData binaryData = ((ContentType)content.ContentHandler).Binary;

            var fileName = content.Name + "Ctd.xml";
            //var fsPath = Path.Combine(context.ContentTypeDirectory, fileName);
            var fsPath = Path.Combine(context.SourceFsPath, fileName);
            var ctdString = GetCtdXml(content["Type"]?.ToString());

            Stream source = null;
            using (FileStream target = new FileStream(fsPath, FileMode.Create))
            {
                byte[] byteArray = Encoding.UTF8.GetBytes(ctdString);
                for (var i = 0; i < source.Length; i++)
                    target.WriteByte((byte)source.ReadByte());
            }
        }

        private static void ExportContentType(string contentName, string contentType, ExportContext context)
        {
            //BinaryData binaryData = ((ContentType)content.ContentHandler).Binary;

            var fileName = contentName + "Ctd.xml";
            //var fsPath = Path.Combine(context.ContentTypeDirectory, fileName);
            var fsPath = Path.Combine(context.ContentTypeDirectory, fileName);
            var ctdString = GetCtdXml(contentType);

            using (FileStream target = new FileStream(fsPath, FileMode.Create))
            {
                byte[] byteArray = Encoding.UTF8.GetBytes(ctdString);
                foreach (var bt in byteArray)
                {
                    target.WriteByte(bt);
                }
            }
        }

        public static string GetDefaultExtension(string mimeType)
        {
            string result;
            RegistryKey key;
            object value;

            key = Registry.ClassesRoot.OpenSubKey(@"MIME\Database\Content Type\" + mimeType, false);
            value = key != null ? key.GetValue("Extension", null) : null;
            result = value != null ? value.ToString() : string.Empty;

            return result;
        }
        [DllImport("urlmon.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false)]
        static extern int FindMimeFromData(
            IntPtr pBC,
            [MarshalAs(UnmanagedType.LPWStr)] string pwzUrl,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I1, SizeParamIndex=3)]
                        byte[] pBuffer,
            int cbSize,
            [MarshalAs(UnmanagedType.LPWStr)] string pwzMimeProposed,
                int dwMimeFlags,
            out IntPtr ppwzMimeOut,
            int dwReserved);

        public static string GetMime(byte[] header)
        {
            string result = string.Empty;
            try
            {
                IntPtr mimetype;
                if (FindMimeFromData(IntPtr.Zero,null,header,(int)header.Length,null,0x20,out mimetype,0) == 0)
                {
                    result = Marshal.PtrToStringUni(mimetype);
                    Marshal.FreeCoTaskMem(mimetype);
                }
            }
            catch (Exception e)
            {
                Log.Error($"{e.Message}");
            }
            return result;
        }

        public static string GetCtdXml(string contentTypeName)
        {
            var ctdContent = ContentTypeContents.FirstOrDefault(c => c.Name == contentTypeName);

            string ctd = null;
            RESTCaller.GetStreamResponseAsync(ctdContent.Id, async response =>
            {
                if (response == null)
                    return;
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream))
                    ctd = reader.ReadToEnd();
            }, CancellationToken.None).GetAwaiter().GetResult();

            return ctd;
        }

        public static List<JObject> GetCtds()
        {
            var schemaRequest = new ODataRequest() { Path = "/Root", ActionName = "GetSchema", AutoFilters = FilterStatus.Disabled, LifespanFilter = FilterStatus.Disabled, Metadata = MetadataFormat.None };
            var schemaResult = RESTCaller.GetResponseStringAsync(schemaRequest).GetAwaiter().GetResult();
            var schemas = Newtonsoft.Json.JsonConvert.DeserializeObject(schemaResult) as JArray;

            return schemas.ToObject<List<JObject>>();
        }

        public static List<JObject> GetFields(List<JObject> schemas)
        {
            List<JObject> result = new List<JObject>();
            //var schemaRequest = new ODataRequest() { Path = "/Root", ActionName = "GetSchema", AutoFilters = FilterStatus.Disabled, LifespanFilter = FilterStatus.Disabled, Metadata = MetadataFormat.None };
            //var schemaResult = RESTCaller.GetResponseStringAsync(schemaRequest).GetAwaiter().GetResult();
            //var schemas = Newtonsoft.Json.JsonConvert.DeserializeObject(schemaResult) as JArray;
            foreach (var ctd in schemas)
            {
                result.AddRange(ctd["FieldSettings"].ToObject<List<JObject>>());
            }


            return result;
        }

        public static JObject LoadAsyncJObject(string actionUrl, ServerContext server)
        {
            JObject result = null;
            
            RESTCaller.ProcessWebResponseAsync(actionUrl, System.Net.Http.HttpMethod.Get, server, async response =>
            {
                if (response == null)
                    return;
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream))
                {
                    var textResponse = reader.ReadToEnd();
                    result = Newtonsoft.Json.JsonConvert.DeserializeObject(textResponse) as JObject;
                }
            }, CancellationToken.None).GetAwaiter().GetResult();

            return result?["d"] as JObject;
        }

        public static JObject LoadContainerAsyncJObject(string actionUrl, ServerContext server)
        {
            JObject result = null;

            RESTCaller.ProcessWebResponseAsync(actionUrl, System.Net.Http.HttpMethod.Get, server, async response =>
            {
                if (response == null)
                    return;
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream))
                {
                    var textResponse = reader.ReadToEnd();
                    result = Newtonsoft.Json.JsonConvert.DeserializeObject(textResponse) as JObject;
                }
            }, CancellationToken.None).GetAwaiter().GetResult();

            return result["d"] as JObject;
        }
    }
}
