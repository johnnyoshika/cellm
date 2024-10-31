﻿using System.ComponentModel;
using MediatR;

namespace Cellm.Tools.Glob;

[Description("Search for files on the user's disk using glob patterns. Useful when user asks you to find files.")]
internal record GlobRequest(
    [Description("The root directory to start the glob search from")] string RootPath,
    [Description("List of patterns to include in the search")] List<string> IncludePatterns,
    [Description("Optional list of patterns to exclude from the search")] List<string>? ExcludePatterns) : ITool, IRequest<GlobResponse>;