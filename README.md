# TagImporter – RA EtherNet/IP Design-Time Tag Import

`TagImporter` is a FactoryTalk Optix **design-time** NetLogic. It imports tags **online** from the PLC configured on an RA EtherNet/IP station. You can optionally import only tags whose data type is one of a list of UDTs.

Requires FactoryTalk Optix **1.8.x or later**. This works only at design time, not in a running application.

---

## 1. Optix Studio setup

### Communication driver and station
The script uses this fixed station path:

```
CommDrivers/RAEtherNet_IPDriver1/RAEtherNet_IPStation1
```

- Add an **RA EtherNet/IP** driver named `RAEtherNet_IPDriver1`.
- Add a station under it named `RAEtherNet_IPStation1`.
- Set the station's connection settings (IP address/route) so Studio can reach the PLC online.
- If your names differ, change the path in `ImportTags()` in `TagImporter.cs`.

### NetLogic object
1. In the project tree, right-click **NetLogic** and choose **New > Design-time NetLogic**, then name it `TagImporter`. The class name in the code is `TagImporter`.
2. Add these variables to the NetLogic object. The names are case-sensitive.

| Variable name | Data type | Description |
|---------------|-----------|-------------|
| `UDTFilter`   | String    | Semicolon-separated list of UDT names to import, e.g. `MyUDT1;MyUDT2`. Matching is case-insensitive and ignores spaces around names. **Leave blank to import all tags.** |
| `Destructive` | Boolean   | `False` = keep tags already imported under the station. `True` = delete **all** tags under the station's `Tags` folder before importing. |

If either variable is missing, the import doesn't start and an error is logged.

---

## 2. Running the import

1. Set `UDTFilter` and `Destructive` on the NetLogic object.
2. Make sure Studio can reach the PLC on the network.
3. Right-click the `TagImporter` NetLogic and choose **Execute "ImportTags"**.
4. Watch the **Output** pane for results.

The import runs in the background. If you run `ImportTags` again before the first import finishes, the second run is skipped and a warning is logged.

---

## 3. Behavior details

### UDT filtering
- Each tag is matched by its PLC data type. The importer reports types like `x-ra/clx/myudt1#datatype`, and the script reduces this to `myudt1` before comparing it with `UDTFilter`.
- A matching UDT tag is imported with all its members, including nested structures and array elements.
- Program-scoped tags (`Program:<name>.<tag>`) are supported. Program container folders are always accepted so the importer can look inside them, so empty program folders may appear.

### Destructive = False (keep existing tags)
- Every tag already under `RAEtherNet_IPStation1/Tags` is recorded by its PLC tag name (`SymbolName`) and included in the import, so it is kept.
- Existing tags are re-imported from the PLC. Edits you made to them in Optix may be overwritten.
- Existing tags that **no longer exist in the PLC** will be removed by the importer.
- If an existing tag has no `SymbolName`, the import is aborted to avoid losing it.

### Destructive = True (wipe and overwrite)
- Every node under the station's `Tags` folder is deleted, then the filtered import runs.
- **There is no rollback.** If the import fails (e.g. the PLC can't be reached), the station is left with no tags. Save or commit your project before using this mode.

---

## 4. Log messages

| Message | Meaning |
|---------|---------|
| `Matched N UDT tag(s).` | The filter found N tags of the listed UDT types. |
| `No PLC tags matched UDTFilter '...'. Types reported by the importer: ...` | Nothing matched. Compare your filter with the listed type names (the part between the last `/` and `#`). |
| `Tag import completed successfully.` | The import finished. |
| `A tag import is already in progress.` | An import is still running; wait for it to finish. |
| `LogicObject must contain UDTFilter and Destructive variables.` | One or both variables are missing or misnamed. |
| `The configured RA Ethernet/IP station was not found.` | The driver or station name doesn't match the path in the code. |
| `Tag import failed: ...` | Error from the importer (often a connection problem). |

---

## 5. Tips
- Save or commit the project before each import, especially with `Destructive = True`.
- To add more UDTs later without losing what you have, set `Destructive = False`, then list the new UDTs in `UDTFilter`.
- Reference: [NetLogic CheatSheet – Design time import of PLC tags](https://github.com/FactoryTalk-Optix/NetLogic_CheatSheet/blob/main/pages/designtime-tags-import.md)
