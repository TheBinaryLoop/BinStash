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

using BinStash.Contracts.Release;
using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Models;

namespace BinStash.Core.Ingestion.Execution;

public sealed class ReleaseIngestionEngine : IReleaseIngestionEngine
{
    private readonly IInputFormatDetector _formatDetector;
    private readonly IIngestionPlanner _planner;
    private readonly IReadOnlyDictionary<string, IInputFormatHandler> _handlers;

    public ReleaseIngestionEngine(IInputFormatDetector formatDetector, IIngestionPlanner planner, IEnumerable<IInputFormatHandler> handlers)
    {
        _formatDetector = formatDetector;
        _planner = planner;
        
        var map = new Dictionary<string, IInputFormatHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (var handler in handlers)
        {
            foreach (var formatId in handler.SupportedFormatIds)
            {
                map[formatId] = handler;
            }
        }

        _handlers = map;
    }

    public async Task<IngestionResult> IngestAsync(IReadOnlyCollection<InputItem> inputs, IReadOnlyDictionary<string, Component> componentMap, CancellationToken ct = default)
    {
        var result = new IngestionResult();
        var context = new IngestionExecutionContext(result);

        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();

            var detectedFormat = await _formatDetector.DetectAsync(input, ct);
            var plan = await _planner.CreatePlanAsync(input, detectedFormat, ct);

            var handler = ResolveHandler(detectedFormat);
            await handler.HandleAsync(input, detectedFormat, plan, context, ct);
        }

        return result;
    }

    private IInputFormatHandler ResolveHandler(DetectedFormat detectedFormat)
    {
        if (_handlers.TryGetValue(detectedFormat.FormatId, out var exact))
            return exact;

        if (_handlers.TryGetValue("file", out var plain))
            return plain;

        throw new InvalidOperationException($"No input format handler registered for format '{detectedFormat.FormatId}', and no fallback 'file' handler exists.");
    }
}