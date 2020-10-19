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
		private const string ContentTypesFolderPath = "/Root/System/Schema/ContentTypes";
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

			if (ContentTypesFolderPath.StartsWith(sourceRepositoryPath))
            {
				var lastSlash = sourceRepositoryPath.LastIndexOf("/");
				var relativePath = ContentTypesFolderPath.Substring(lastSlash + 1);
				ContentTypeDirectory = Path.Combine(targetFsPath, relativePath);
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