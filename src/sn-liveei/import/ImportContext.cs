using System.Collections.Generic;
using System.Xml;
using System.IO;
using SenseNet.Configuration;

namespace SnLiveExportImport
{
	public class ImportContext
	{
		public string CurrentDirectory { get; private set; }
		public XmlNodeList FieldData { get; private set; }
		public bool IsNewContent { get; private set; }
		public bool NeedToValidate { get; private set; }
		public string ErrorMessage { get; set; }
		public bool UpdateReferences { get; set; }
		public List<string> PostponedReferenceFields { get; private set; }
		public bool HasReference
		{
			get { return PostponedReferenceFields.Count > 0; }
		}
		public string ContentType { get; set; }

		public ImportContext(XmlNodeList fieldData, string contentType, string currentDirectory, bool isNewContent, bool needToValidate, bool updateReferences)
		{
			CurrentDirectory = currentDirectory;
			FieldData = fieldData;
			IsNewContent = isNewContent;
			NeedToValidate = needToValidate;
			UpdateReferences = updateReferences;
			ContentType = contentType;
			PostponedReferenceFields = new List<string>();
		}

		public string UnescapeFileName(string path)
		{
			return path.Replace("$amp;", "&");

		}
	}
}