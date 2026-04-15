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
    using IOFile = System.IO.File;

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
                if (Program._appConfig.ExcludedImportFields.Any(x => string.Equals(x, fieldName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // TODO: check if xmlnodelist is only available with reference field or should filter for Path nodes
                var pathNodeList = fieldNode.SelectNodes("*");
                
                string[] setFirstTime = { "CreatedBy", "ModifiedBy" };
                string[] arrayTypes = { "MemoType", "EventType", "Priority", "Status" };
                if (!context.UpdateReferences)
                {
                    // Password field: extract Text or Hash value from child nodes.
                    // Sending as plain string routes through field.SetData() on the server (JValue path),
                    // which properly marks the field as changed. Sending as JObject bypasses SetData
                    // (uses user.Password directly + continue) and validation fails because
                    // PasswordField.GetData() returns null outside WorkingMode.Importing.
                    if (string.Equals(fieldName, "Password", StringComparison.OrdinalIgnoreCase))
                    {
                        var textNode = fieldNode.SelectSingleNode("Text");
                        var hashNode = fieldNode.SelectSingleNode("Hash");
                        string passwordValue = textNode?.InnerText ?? hashNode?.InnerText;

                        Log.Debug($"Password field detected for {content.Name}: XML=<{fieldNode.InnerXml}>, value={passwordValue?.Substring(0, Math.Min(passwordValue?.Length ?? 0, 6))}...");

                        if (!string.IsNullOrWhiteSpace(passwordValue))
                        {
                            content[fieldName] = passwordValue;
                            isModified = true;
                        }
                    }
                    // reference field (I hope)
                    else if (pathNodeList.Count > 0) //if (fieldNode.InnerXml.StartsWith("<Path>"))
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
                                string cntType = content["Type"].ToString();
                                string filePath = Path.Combine(context.CurrentDirectory, attachment);
                                using (FileStream fs = IOFile.OpenRead(filePath))
                                {
                                    if (fs.Length > 0)
                                    {
                                        var repository = content.Repository ?? throw new InvalidOperationException("Repository access is not available during binary upload.");

                                        var uploadResult = repository.UploadAsync(new UploadRequest
                                        {
                                            ParentPath = content.ParentPath,
                                            ContentName = content.Name,
                                            ContentType = cntType,
                                            PropertyName = fieldName,
                                            FileName = content.Name
                                        }, fs, CancellationToken.None).GetAwaiter().GetResult();

                                        if (uploadResult != null)
                                        {
                                            if (uploadResult.Id > 0)
                                                content = repository.LoadContentAsync(uploadResult.Id, CancellationToken.None).GetAwaiter().GetResult();
                                            else if (!string.IsNullOrWhiteSpace(uploadResult.Url))
                                                content = repository.LoadContentAsync(uploadResult.Url, CancellationToken.None).GetAwaiter().GetResult();
                                        }

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
                            //content[fieldName] = fieldNode.InnerText;
                            //content[fieldName] = new string[] { fieldNode.InnerText };
                            //content[fieldName] = new Newtonsoft.Json.Linq.JArray(new string[] { fieldNode.InnerText });
                            content[fieldName] = new[] { fieldNode.InnerText };
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
                        //content[fieldName] = $"['{string.Join("','",paths)}']";
                        content[fieldName] = paths.ToArray();
                        isModified = true;
                    } 
                }
            }
            return isModified;
        }
    }
}
