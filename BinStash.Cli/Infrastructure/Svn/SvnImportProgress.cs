// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using CliFx.Infrastructure;
using Spectre.Console;

namespace BinStash.Cli.Infrastructure.Svn;

public sealed class SvnImportProgress
{
    private readonly IAnsiConsole _console;
    private readonly Lock _sync = new();

    public SvnImportProgress(IConsole console)
    {
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(console.Output)
        });
    }

    public void Info(string message)
    {
        lock (_sync)
            _console.MarkupLine($"[grey]INFO:[/] {Escape(message)}");
    }

    public void Success(string message)
    {
        lock (_sync)
            _console.MarkupLine($"[green]OK:[/] {Escape(message)}");
    }

    public void Warn(string message)
    {
        lock (_sync)
            _console.MarkupLine($"[yellow]WARN:[/] {Escape(message)}");
    }

    public void Error(string message)
    {
        lock (_sync)
            _console.MarkupLine($"[red]ERR:[/] {Escape(message)}");
    }

    public async Task RunStatusAsync(string initialStatus, Func<StatusContext, Task> action)
    {
        await _console.Status()
                .AutoRefresh(true)
                .Spinner(Spinner.Known.Dots)
                .StartAsync(initialStatus, action);
    }

    private static string Escape(string value) => Markup.Escape(value);
}