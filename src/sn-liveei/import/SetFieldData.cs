using SenseNet.Client;
using Serilog;
using SnLiveExportImport;
using SnLiveExportImport.ContentImporter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;
using System.Xml;

namespace SnLiveExportImport
{
    public static class ContentMetaData
    {
        public static bool SetFields(Content content, ImportContext context)
        {
            bool isModified = false;
            // import fields
            foreach (XmlNode fieldNode in context.FieldData)
            {
                var fieldName = fieldNode.LocalName;

                var attachment = ((XmlElement)fieldNode).GetAttribute("attachment");

                // TODO: test aspect fields if have to created first

                // Technical field so skip it
                if (fieldName == "Aspects")
                    continue;

                if (context.ContentType == "Folder" && fieldName == "AllowedChildTypes")
                    continue;

                // TODO: special types wont work for now
                if (Program._appConfig.ExcludedImportFields.Any(x => x == fieldName))
                    continue;

                // TODO: check if xmlnodelist is only available with reference field or should filter for Path nodes
                var pathNodeList = fieldNode.SelectNodes("*");
                
                string[] setFirstTime = { "CreatedBy", "ModifiedBy" };
                string[] arrayTypes = { "MemoType", "EventType", "Priority", "Status" };
                if (!context.UpdateReferences)
                {
                    // reference field (I hope)
                    if (pathNodeList.Count > 0) //if (fieldNode.InnerXml.StartsWith("<Path>"))
                    {
                        // some reference field must be set at first run
                        if (setFirstTime.Any(x => x == fieldName))
                        {
                            if (content.Id == 0)
                            {
                                // what if referenced content does not exists, later can not be set?
                                if (!string.IsNullOrWhiteSpace(fieldNode.InnerText))
                                {
                                    content[fieldName] = fieldNode.InnerText;
                                    isModified = true;
                                }
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // set other references postponed for ay second round
                            if (pathNodeList.Count > 0 || fieldNode.InnerText.Trim().Length > 0)
                            {
                                context.PostponedReferenceFields.Add(fieldName);
                                isModified = true;
                            }
                        }
                    }
                    else
                    if (fieldName == "AllowedChildTypes")
                    {
                        string[] notAllowedToModify = { "SystemFolder" };
                        if (!notAllowedToModify.Any(x => x == context.ContentType))
                        {
                            // TODO: this not works, how we can send allowedchildtypes
                            content[fieldName] = fieldNode.InnerText.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            isModified = true;
                        }
                    }
                    else if (arrayTypes.Any(x => x == fieldName))
                    {
                        content[fieldName] = fieldNode.InnerText.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        isModified = true;
                    }
                    else
                    // attachment means binary in given file, so we will upload it
                    if (!string.IsNullOrWhiteSpace(attachment))
                    {
                        // but if new content, it's already uploaded at creation
                        if (!context.IsNewContent)
                        {
                            try
                            {
                                string filePath = Path.Combine(context.CurrentDirectory, attachment);
                                using (FileStream fs = File.OpenRead(filePath))
                                {
                                    if (fs.Length > 0)
                                    {
                                        content = Content.UploadAsync(content.ParentPath, content.Name, fs, null, fieldName).GetAwaiter().GetResult();
                                        Thread.Sleep(100);
                                        Log.Information($"Upload at SetFieldData: {content.Name}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Error at binary update: {content.Name}, {attachment}, {ex.Message}, {ex.InnerException?.Message}");
                                //Thread.Sleep(1000);
                            }
                        }
                    }
                    else
                    {
                        // Simple types (Name, DisplayName, Body, Int, Date, single Reference) all works with innertext
                        if (!string.IsNullOrWhiteSpace(fieldNode.InnerText))
                        {                            
                            content[fieldName] = fieldNode.InnerText;
                            isModified = true;
                        }
                    }
                }
                else
                {
                    if (setFirstTime.Any(x => x == fieldName))
                    {
                        continue;
                    }
                    else if (pathNodeList.Count == 0)
                    {
                        continue;
                    } 
                    else if (pathNodeList.Count == 1)
                    {
                        if (!string.IsNullOrWhiteSpace(fieldNode.InnerText))
                        {
                            content[fieldName] = fieldNode.InnerText;
                            isModified = true;
                        }
                    } 
                    else if (pathNodeList.Count > 1)
                    {
                        // TODO: test with multiple reference
                        List<string> paths = new List<string>();
                        foreach (System.Xml.XmlNode pathNode in pathNodeList)
                        {
                            paths.Add(pathNode.InnerText.Trim());
                        }
                        content[fieldName] = $"[{string.Join(",",paths)}]";
                        isModified = true;
                    } 
                }
            }
            return isModified;
        }
    }
}
