using SenseNet.Client;
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

                // if (fieldNode.InnerXml.StartsWith("<Path>"))
                // {
                //     var isReference = true;
                // }
                // else
                if (fieldName == "AllowedChildTypes")
                {
                    string[] notAllowedToModify = { "SystemFolder" };
                    if (!notAllowedToModify.Any(x => x == context.ContentType))
                    {
                        content[fieldName] = fieldNode.InnerText.Split(", ");
                    }
                } else
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

            return true;
        }
    }
}
