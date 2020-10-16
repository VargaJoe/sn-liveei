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
        public static List<JObject> ContentFields { get; set; }
        public static string[] excludeFields = { "ParentId", "Id", "Name", "Version", "VersionId", 
            "Path", "Depth", "Type", "TypeIs", "InTree", "InFolder", "IsSystemContent", "HandlerName", 
            "ParentTypeName", "CreatedById", "ModifiedById", "AllFieldSettingContents", "OwnerId", 
            "EffectiveAllowedChildTypes", "VersioningMode", "AllRoles", "DirectRoles", "CheckedOutTo", 
            "InheritableVersioningMode", "VersionCreatedBy", "VersionCreationDate", "VersionModifiedBy", 
            "VersionModificationDate", "ApprovingMode", "InheritableApprovingMode", "SavingState", 
            "ExtensionData", "BrowseApplication", "Versions", "CheckInComments", "RejectReason", 
            "Workspace", "BrowseUrl", "Sharing", "SharedWith", "SharedBy", "SharingMode", "SharingLevel", 
            "Actions", "IsFile", "Children", "Publishable", "Locked", "Rate", "RateStr", "Tags", "Approvable",
            "AllowedChildTypes", "IsFolder", "Icon", "WorkspaceSkin", "AvailableViews", "FieldSettingContents", 
            "AvailableContentTypeFields", "OwnerWhenVisitor" };
        public static string[] fileTypes = { "File", "Image" };

        public static void StartExport()
        {
            // prepare ctd info
            ContentTypes = GetCtds();

            // prepare field info
            ContentFields = GetFields(ContentTypes);

            // TODO: get variables from parameters and/or settings
            //string sourceRepoPath = "/Root/Content";
            string sourceRepoPath = "/Root/Content/SampleWorkspace2";
            string targetBasePath = "./export";
            //string fsTargetRepoPath = $".{sourceRepoPath.Replace("/Root", "")}";

            string queryPath = string.Empty;
            bool all = true;
            bool sync = true;

            //string combino = Path.Combine(targetBasePath, fsTargetRepoPath);
            string cbPath = (sync) ? targetBasePath : $"{targetBasePath}{DateTime.Now.Ticks}";
            string fsPath = Path.GetFullPath(cbPath);

            //var query = $"+Type:ContentType";
            //ContentTypes = Content.QueryAsync(query, new string[0], new string[0], new QuerySettings() { EnableAutofilters = FilterStatus.Disabled }).GetAwaiter().GetResult().ToList();

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
            if (fileTypes.Any(f => f == contentType))
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

        private static void ExportContent(Content content, ExportContext context, string fsPath, string indent)
        {
            //if (content.ContentHandler is ContentType)
            //{
            //    //Log
            //    Log.Information(content.Path, content.Name);
            //    //Log

            //    ExportContentType(content, context, indent);
            //    return;
            //}

            context.CurrentDirectory = fsPath;

            //Log
            Log.Information(content.Path, content.Name);
            //Log

            string metaFilePath = Path.Combine(fsPath, content.Name + ".Content");
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
                writer.WriteElementString("ContentType", content["Type"].ToString());
                writer.WriteElementString("ContentName", content.Name);
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
                //content.ContentHandler.Security.ExportPermissions(writer);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        private static void ExportContent(JObject content, ExportContext context, string fsPath, string indent)
        {

            //if (content.ContentHandler is ContentType)
            //{
            //    //Log
            //    Log.Information(content.Path, content.Name);
            //    //Log

            //    ExportContentType(content, context, indent);
            //    return;
            //}

            context.CurrentDirectory = fsPath;
            var contentPath = content?["Path"]?.ToString();
            var contentName = content?["Name"]?.ToString();
            //Log
            Log.Information(contentPath, contentName);
            //Log

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
                //content.ContentHandler.Security.ExportPermissions(writer);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        public static void ExportFieldData(Content content, XmlWriter writer, ExportContext context)
        {
            // first of all: exporting aspect names
            if (content != null)
            {
                var aspects = content["Aspects"] as string[];
                if (aspects != null && aspects.Length > 0)
                    writer.WriteElementString("Aspects", String.Join(", ", aspects));
            }



            //foreach (JProperty f in test["d"])
            //{
            //    var fieldName = f.Name;
            //    var fieldValue = f.Value;
            //}

            // exporting other fields
            //if (this.ContentHandler is ContentType)
            //    return;
            // exit if content type is "ContentType" 

            //var fields = content.GetType().GetProperties();

            List<string> fields = GetCtd(content);

            //var fields = content["Fields"];
            foreach (var field in fields)
            {                
                var value = content[field] as JObject;
                //if (field.Name != "Name" && field.Name != "Versions")
                if (!excludeFields.Any(f => f == field)                    
                    //&& value != null && !string.IsNullOrWhiteSpace(value?.ToString())
                    )
                {
                    //if (ReadOnly)
                    //    return;
                    //if (GetData() == null)
                    //    return;

                    //if (!HasExportData)
                    //    return;

                    //FieldSubType subType;
                    //var exportName = GetExportName(this.Name, out subType);

                    //writer.WriteStartElement(exportName);
                    writer.WriteStartElement(field);
                    //if (subType != FieldSubType.General)
                    //    writer.WriteAttributeString(FIELDSUBTYPEATTRIBUTENAME, subType.ToString());

                    //ExportData(writer, context);
                    //var d = ((JObject)content[field])["__deferred"]["uri"];
                    if (value != null && value["__deferred"] != null)
                    {
                        // reference value
                        var deferredUri = value["__deferred"]["uri"]?.ToString();
                        var baseUrl = content.Server.Url;

                        object refResponse = null;
                        RESTCaller.ProcessWebResponseAsync($"{baseUrl}{deferredUri}", System.Net.Http.HttpMethod.Get, content.Server, async response =>
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

                        if (refResponse is JObject)
                        {
                            writer.WriteStartElement("Path");
                            writer.WriteString((refResponse as JObject)?["d"]?["Path"]?.ToString());
                            writer.WriteEndElement();
                        }
                        else if (refResponse is JArray)
                        {
                            foreach (var refField in refResponse as JArray)
                            {
                                writer.WriteStartElement("Path");
                                writer.WriteString((refField as JObject)?["Path"]?.ToString());
                                writer.WriteEndElement();
                            }
                        }

                    }
                    else
                    {
                        writer.WriteString(content[field]?.ToString());
                    }

                    writer.WriteEndElement();
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


            foreach (JProperty field in content.Properties())
            {
                var fieldName = field.Name;
                var fieldValue = field.Value;

                if (!excludeFields.Any(f => f == fieldName))
                {
                    var contentField = ContentFields.FirstOrDefault(cf => cf["Name"]?.ToString() == fieldName);
                    if (contentField == null)
                        continue;

                    var fieldType = contentField["Type"]?.ToString().Replace("FieldSetting", "");

                    if (string.IsNullOrWhiteSpace(fieldType))
                        continue;

                    //if (ReadOnly)
                    //    return;

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
                                    foreach (var refField in refResults)
                                    {
                                        writer.WriteStartElement("Path");
                                        writer.WriteString((refField as JObject)?["Path"]?.ToString());
                                        writer.WriteEndElement();
                                    }
                                }
                                else
                                {
                                    // only single reference
                                    writer.WriteStartElement("Path");
                                    writer.WriteString((refResponse as JObject)?["d"]?["Path"]?.ToString());
                                    writer.WriteEndElement();
                                }
                                writer.WriteEndElement();
                            }
                            break;
                        case "ShortText":
                        case "LongText":
                        case "Integer":
                        case "Number":
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
                        case "Null":
                            // not exported
                            break;
                        default:
                            Log.Warning($"NOT IMPLEMENTED: {fieldType}");
                            break;
                    }
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

        public static List<string> GetCtd(Content content)
        {
            //var ctdContent = ContentTypes.FirstOrDefault(c => c.Name == content["Type"].ToString());

            //string ctd = null;
            //RESTCaller.GetStreamResponseAsync(ctdContent.Id, async response =>
            //{
            //    if (response == null)
            //        return;
            //    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            //    using (var reader = new StreamReader(stream))
            //        ctd = reader.ReadToEnd();
            //}, CancellationToken.None).GetAwaiter().GetResult();

            List<string> result = new List<string>();

            var a = new ODataRequest() { Path = "/Root", ActionName = "GetSchema", AutoFilters = FilterStatus.Disabled, LifespanFilter = FilterStatus.Disabled, Metadata = MetadataFormat.None };
            var c = RESTCaller.GetResponseStringAsync(a).GetAwaiter().GetResult();
            //var d = JsonHelper.Deserialize(c.Substring(1, c.Length - 2).Replace("\n", "").Trim());
            var d = Newtonsoft.Json.JsonConvert.DeserializeObject(c) as JArray;

            var jo = d.FirstOrDefault();
            var jfs = jo["FieldSettings"];

            foreach (JObject setting in jfs)
            {
                result.Add(setting["Name"].ToString());
            }

            return result;
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
