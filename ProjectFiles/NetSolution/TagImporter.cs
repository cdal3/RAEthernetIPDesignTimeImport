#region Using directives
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.CoreBase;
using FTOptix.NativeUI;
using FTOptix.Retentivity;
using FTOptix.Core;
using FTOptix.NetLogic;
using FTOptix.RAEtherNetIP;
using FTOptix.CommunicationDriver;
#endregion

public class TagImporter : BaseNetLogic
{
    private int importInProgress;

    [ExportMethod]
    public void ImportTags()
    {
        if (Interlocked.CompareExchange(ref importInProgress, 1, 0) != 0)
        {
            Log.Warning(nameof(TagImporter), "A tag import is already in progress.");
            return;
        }

        try
        {
            var station = Project.Current.Get<FTOptix.RAEtherNetIP.Station>(
                "CommDrivers/RAEtherNet_IPDriver1/RAEtherNet_IPStation1");
            if (station == null)
                throw new InvalidOperationException("The configured RA Ethernet/IP station was not found.");

            var filterVariable = LogicObject.GetVariable("UDTFilter");
            var destructiveVariable = LogicObject.GetVariable("Destructive");
            if (filterVariable == null || destructiveVariable == null)
                throw new InvalidOperationException("LogicObject must contain UDTFilter and Destructive variables.");

            string filterText = filterVariable.Value;
            bool destructive = destructiveVariable.Value;
            var typeNames = new HashSet<string>(
                (filterText ?? string.Empty).Split(';')
                    .Select(NormalizeTypeName)
                    .Where(typeName => typeName.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(filterText) && typeNames.Count == 0)
                throw new InvalidOperationException("UDTFilter contains no UDT names.");

            _ = ImportTagsAsync(station, typeNames, destructive);
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref importInProgress, 0);
            Log.Error(nameof(TagImporter), "Unable to start tag import: " + exception.Message);
        }
    }

    private async Task ImportTagsAsync(FTOptix.RAEtherNetIP.Station station,
        HashSet<string> typeNames, bool destructive)
    {
        try
        {
            var tags = station.Get("Tags");
            if (tags == null)
                throw new InvalidOperationException("The station's Tags folder was not found.");

            var existingSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!destructive)
                CollectExistingSymbols(tags, existingSymbols);

            if (destructive)
            {
                foreach (var child in tags.Children.ToArray())
                    child.Delete();
            }

            var matchingSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var observedTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var tagImporter = station.TagImporter;
            tagImporter.Mode = FetchMode.Online;
            // The importer removes any tag the filter rejects, so existing tags must be re-selected to be kept.
            await tagImporter.Import((symbolName, typeName) =>
            {
                typeName = NormalizeTypeName(typeName);
                observedTypes.Add(typeName.Length > 0 ? typeName : "<empty>");

                if (existingSymbols.Any(existing => IsSameOrDescendant(symbolName, existing) ||
                    IsSameOrDescendant(existing, symbolName)))
                    return true;

                if (typeNames.Count == 0)
                    return true;

                if (typeNames.Contains(typeName))
                {
                    matchingSymbols.Add(symbolName);
                    return true;
                }

                if (matchingSymbols.Any(matching => IsSameOrDescendant(symbolName, matching)))
                    return true;

                // Program containers must be accepted so program-scoped UDT instances are enumerated.
                return symbolName.StartsWith("Program:", StringComparison.OrdinalIgnoreCase) &&
                    symbolName.IndexOf('.') < 0;
            });

            if (typeNames.Count > 0 && matchingSymbols.Count == 0)
                Log.Warning(nameof(TagImporter), "No PLC tags matched UDTFilter '" + string.Join(";", typeNames) +
                    "'. Types reported by the importer: " + string.Join(";", observedTypes));
            else if (typeNames.Count > 0)
                Log.Info(nameof(TagImporter), "Matched " + matchingSymbols.Count + " UDT tag(s).");
            Log.Info(nameof(TagImporter), "Tag import completed successfully.");
        }
        catch (OperationCanceledException)
        {
            Log.Warning(nameof(TagImporter), "Tag import was canceled.");
        }
        catch (Exception exception)
        {
            Log.Error(nameof(TagImporter), "Tag import failed: " + exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref importInProgress, 0);
        }
    }

    private static void CollectExistingSymbols(IUANode parent, HashSet<string> symbols)
    {
        foreach (var child in parent.GetOwnedNodes())
        {
            if (child is FTOptix.RAEtherNetIP.Tag tag)
            {
                if (string.IsNullOrWhiteSpace(tag.SymbolName))
                    throw new InvalidOperationException("An existing tag has no SymbolName; import aborted to preserve it.");
                symbols.Add(tag.SymbolName);
            }
            else if (child is IUAObject)
            {
                CollectExistingSymbols(child, symbols);
            }
        }
    }

    private static string NormalizeTypeName(string typeName)
    {
        typeName = (typeName ?? string.Empty).Trim();
        // The importer reports types as URIs, e.g. "x-ra/clx/myudt1#datatype".
        var hash = typeName.IndexOf('#');
        if (hash >= 0)
            typeName = typeName.Substring(0, hash);
        var slash = typeName.LastIndexOf('/');
        if (slash >= 0)
            typeName = typeName.Substring(slash + 1);
        // Array types may be reported with dimensions, e.g. "Motor[10]".
        var bracket = typeName.IndexOf('[');
        if (bracket >= 0)
            typeName = typeName.Substring(0, bracket);
        var colon = typeName.LastIndexOf(':');
        if (colon >= 0)
            typeName = typeName.Substring(colon + 1);
        return typeName.Trim();
    }

    private static bool IsSameOrDescendant(string symbolName, string parentSymbol)
    {
        return string.Equals(symbolName, parentSymbol, StringComparison.OrdinalIgnoreCase) ||
            symbolName.StartsWith(parentSymbol + ".", StringComparison.OrdinalIgnoreCase) ||
            symbolName.StartsWith(parentSymbol + "[", StringComparison.OrdinalIgnoreCase);
    }
}
