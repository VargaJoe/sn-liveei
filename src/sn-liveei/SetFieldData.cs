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
        public enum FieldSubType
        {
            General, ContentList
        }

        public static bool SetFields(Content content, ImportContext context)
        {
            bool changed = context.IsNewContent;
            
            foreach (XmlNode fieldNode in context.FieldData)
            {
                var subType = FieldSubType.General;
                var subTypeString = ((XmlElement)fieldNode).GetAttribute("subType");
                if (subTypeString.Length > 0)
                    subType = (FieldSubType)Enum.Parse(typeof(FieldSubType), subTypeString);
                var fieldName = fieldNode.LocalName; //Field.ParseImportName(fieldNode.LocalName, subType);

                // This field has already imported or skipped
                if (fieldName == "Aspects")
                    continue;

                var rawValue = fieldNode.InnerXml;
                // TODO: special types, and binary wont work for now
                string[] skipTemporarily = { "AllowedChildTypes", "GroupAttachments", "NotificationMode", "ImageData", "InheritableApprovingMode", "InheritableVersioningMode", "ApprovingMode", "VersioningMode" };
                if (skipTemporarily.Any(x => x == fieldName))
                    continue;

                //Name, DisplayName, Body, Int, Date, single Reference all works with text
                content[fieldName] = fieldNode.InnerText;
            }

            if (!changed)
                return true;

            return true;
        }
    }
}
