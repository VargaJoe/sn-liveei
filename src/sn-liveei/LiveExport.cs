using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Newtonsoft.Json.Linq;
using SenseNet.Client;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Xml;

namespace SnLiveExportImport
{
    public static class LiveExport
    {
        public static List<Content> ContentTypes { get; set; }
        public static string[] excludeFields = { "ParentId", "Id", "Name", "Version", "VersionId", "Path", "Depth", "Type", "TypeIs", "InTree", "InFolder", "IsSystemContent", "HandlerName", "ParentTypeName", "CreatedById", "ModifiedById", "AllFieldSettingContents" };

        public static void StartExport()
        {
            // TODO: get variables from parameters and/or settings
            //string sourceRepoPath = "/Root/Content";
            string sourceRepoPath = "/Root/Content/JoeTest/Marketing/Groups/Visitors";            
            string targetBasePath = "./export";
            //string fsTargetRepoPath = $".{sourceRepoPath.Replace("/Root", "")}";

            string queryPath = string.Empty;
            bool all = true;
            bool sync = true;

            //string combino = Path.Combine(targetBasePath, fsTargetRepoPath);
            string cbPath = (sync) ? targetBasePath : $"{targetBasePath}{DateTime.Now.Ticks}";
            string fsPath = Path.GetFullPath(cbPath);

            var query = $"+Type:ContentType";
            ContentTypes = Content.QueryAsync(query, new string[0], new string[0], new QuerySettings() { EnableAutofilters = FilterStatus.Disabled }).GetAwaiter().GetResult().ToList();

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


                //-- load export root
                var root = Content.LoadAsync(repositoryPath).GetAwaiter().GetResult();

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

                    //if (all)
                    //ExportContentTree(root, context, fullDirPath, "");
                    //else
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
    }
}
