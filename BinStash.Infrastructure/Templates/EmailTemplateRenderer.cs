// Copyright (C) 2025  Lukas Eßmann
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

using System.Collections.Concurrent;
using System.Reflection;
using HandlebarsDotNet;

namespace BinStash.Infrastructure.Templates;

public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private readonly Assembly _assembly;
    private readonly string _rootNamespace;
    private readonly ConcurrentDictionary<string, HandlebarsTemplate<object, object>> _cache = new();

    private IHandlebars Handlebars { get; }
    
    public EmailTemplateRenderer(Assembly assembly, string rootNamespace)
    {
        _assembly = assembly;
        _rootNamespace = rootNamespace;

        // Create an isolated Handlebars environment (recommended for apps)
        Handlebars = HandlebarsDotNet.Handlebars.Create();

        RegisterPartials();
        RegisterHelpers();
    }
    
    public string Render(string templateName, object model)
    {
        var template = _cache.GetOrAdd(templateName, CompileTemplate);
        return template(model);
    }

    private HandlebarsTemplate<object, object> CompileTemplate(string templateName)
    {
        var resourceName = $"{_rootNamespace}.Templates.Emails.{templateName}.hbs";
        var source = ReadResource(resourceName);
        return Handlebars.Compile(source);
    }

    private void RegisterPartials()
    {
        // Convention: Partials start with "_" and live in Templates/Partials
        var prefix = $"{_rootNamespace}.Templates.Partials.";
        foreach (var res in _assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix) && n.EndsWith(".hbs")))
        {
            var partialName = Path.GetFileNameWithoutExtension(res); // e.g. "_Footer"
            var source = ReadResource(res);
            Handlebars.RegisterTemplate(partialName.Replace(prefix, string.Empty), source);
        }
    }

    private void RegisterHelpers()
    {
        // Uppercase helper
        Handlebars.RegisterHelper("upper", (writer, context, parameters) =>
        {
            var s = parameters[0]?.ToString() ?? "";
            writer.WriteSafeString(s.ToUpperInvariant());
        });
    }

    private string ReadResource(string resourceName)
    {
        using var stream = _assembly.GetManifestResourceStream(resourceName)
                         ?? throw new InvalidOperationException($"Template resource not found: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}