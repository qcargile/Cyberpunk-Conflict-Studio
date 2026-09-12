# Source editor integration

Conflict Studio opens source files with an explicit local preference. The choices are the normal Windows editor, Visual Studio Code, and Notepad++. It stores the selected known editor kind and executable path in `%LOCALAPPDATA%\Cyberpunk Conflict Studio\source-editor.json`. It does not store or execute command templates.

Visual Studio Code receives `--goto` followed by one `file:line` argument. This matches the [Visual Studio Code command-line interface](https://code.visualstudio.com/docs/configure/command-line), which defines `--goto` for `file:line[:character]` locations.

Notepad++ receives `-n<line>` followed by the file path. This matches the [Notepad++ command-line documentation](https://github.com/notepad-plus-plus/npp-usermanual/blob/master/content/docs/command-prompt.md), which defines `-n` as the line number for the file path.

Every argument is added through `ProcessStartInfo.ArgumentList`. File paths are never concatenated into a shell command. If the selected executable is missing or cannot start, Conflict Studio opens the file through its existing normal-editor path and reports that the requested line was not selected.
