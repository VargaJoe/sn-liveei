# sn-liveei
experimental


# settings
- execution mode: export or import
- repository path: parent content path of repository content for export from or import to
- local path: file system path for export to or import from
- syncmode: true or false
- true: full path will be mapped on file system, e.g.: ./exportfolder/Root/Content/ExportedContent
  - false: relative path will be mapped on file system, e.g.: ./exportfolder/ExportedContent
- tree: true or false
  - true: given content and all subcontent will be exported
  - false: only given content will be exported
- continuefrom: repository path
  - if given, import (will) continues from given content, previous contents will be skipped (but their references)
