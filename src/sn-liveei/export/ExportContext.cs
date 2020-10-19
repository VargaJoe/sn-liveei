using System.Collections.Generic;
using System.Xml;
using System.IO;
using SenseNet.Configuration;
using SenseNet.Client;
using System;

namespace SnLiveExportImport
{
	public class ExportContext
	{
		private List<string> _outerReferences;
		public string SourceFsPath { get; private set; }
		public string CurrentDirectory { get; set; }
		public string ContentTypeDirectory { get; private set; }

		private string _sourceRoot;

		public ExportContext(string sourceRepositoryPath, string targetFsPath)
		{
			bool isValid = Content.ExistsAsync(sourceRepositoryPath).GetAwaiter().GetResult();
			if (!isValid)
			{
				throw new Exception($"Source path is invalid: {sourceRepositoryPath}");
			}
			SourceFsPath = sourceRepositoryPath;
			CurrentDirectory = targetFsPath;
			_sourceRoot = String.Concat(sourceRepositoryPath, sourceRepositoryPath.EndsWith("/") ? "" : "/");
			_outerReferences = new List<string>();

            if (sourceRepositoryPath == "/Root")
            {
                ContentTypeDirectory = Path.Combine(targetFsPath, "Root/System/Schema/ContentTypes");
            }
            else
            if (sourceRepositoryPath == "/Root/System")
            {
				ContentTypeDirectory = Path.Combine(targetFsPath, "System/Schema/ContentTypes");
			}
			if (sourceRepositoryPath == "/Root/System/Schema")
			{
				ContentTypeDirectory = Path.Combine(targetFsPath, "Schema/ContentTypes");
			}
            else
			if (sourceRepositoryPath == "/Root/System/Schema/ContentTypes")
			{
				ContentTypeDirectory = Path.Combine(targetFsPath, "ContentTypes");
			}
        }
		public void AddReference(string path)
		{
			if (path == SourceFsPath)
				return;
			if (path.StartsWith(_sourceRoot))
				return;
			if (_outerReferences.Contains(path))
				return;
			_outerReferences.Add(path);
		}

		//public ReadOnlyCollection<string> GetOuterReferences()
		//{
		//	return _outerReferences.AsReadOnly();
		//}
	}
}