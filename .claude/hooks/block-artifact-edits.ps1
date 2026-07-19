# PreToolUse hook: blocks Edit/Write to generated build artifacts (bin/, obj/).
# These folders are build outputs - source files are the only thing that should
# ever be edited.
try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
} catch {
    exit 0
}

$path = $payload.tool_input.file_path
if ($path -and $path -match '[\\/](bin|obj)[\\/]') {
    [Console]::Error.WriteLine("Blocked: '$path' is a generated build artifact under bin/ or obj/. Edit the source files instead; MSBuild regenerates these folders.")
    exit 2
}
exit 0
