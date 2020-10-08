﻿using SenseNet.Client;
using Serilog;
using SnLiveExportImport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace SnLiveExportImport
{
    public static class ContentMetaData
    {
        public static bool SetFields(Content content, ImportContext context)
        {
            // import fields
            foreach (XmlNode fieldNode in context.FieldData)
            {
                var fieldName = fieldNode.LocalName;

                var attachment = ((XmlElement)fieldNode).GetAttribute("attachment");

                // TODO: test aspect fields if have to created first

                // Technical field so skip it
                if (fieldName == "Aspects")
                    continue;

                // TODO: special types wont work for now
                string[] skipTemporarily = { "GroupAttachments", "NotificationMode", "InheritableApprovingMode", "InheritableVersioningMode", "ApprovingMode", "VersioningMode" };
                if (skipTemporarily.Any(x => x == fieldName))
                    continue;

                // TODO: check if xmlnodelist is only available with reference field or should filter for Path nodes
                var pathNodeList = fieldNode.SelectNodes("*");
                
                string[] setFirstTime = { "CreatedBy", "ModifiedBy" };
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
                                content[fieldName] = fieldNode.InnerText;
                            } else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // set other references postponed for ay second round
                            if (pathNodeList.Count > 0 || fieldNode.InnerText.Trim().Length > 0)
                                context.PostponedReferenceFields.Add(fieldName);
                        }
                    }
                    else
                    if (fieldName == "AllowedChildTypes")
                    {
                        string[] notAllowedToModify = { "SystemFolder" };
                        if (!notAllowedToModify.Any(x => x == context.ContentType))
                        {
                            content[fieldName] = fieldNode.InnerText.Split(", ");
                        }
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
                                    content = Content.UploadAsync(content.ParentPath, content.Name, fs, null, fieldName).GetAwaiter().GetResult();
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"{content.Name}: {attachment}, {ex.Message}, {ex.InnerException?.Message}");
                            }
                        }
                    }
                    else
                    {
                        // Simple types (Name, DisplayName, Body, Int, Date, single Reference) all works with innertext
                        content[fieldName] = fieldNode.InnerText;

                        // TODO: BUT reference field not should be updated at first round, only after when all the content is present in repository
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
                        content[fieldName] = fieldNode.InnerText;
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
                    } 
                }
            }
            // TODO: check if something modified and only save when true 
            return true;
        }
    }
}
