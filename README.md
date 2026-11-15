# BUG_Reclamation

Two buildings for everything you no longer need. Both burn any item, ammunition and buildings included. The **Trash Converter** turns what it burns into the Dark Fog item you choose, the **Incinerator** turns it into power.

Both are unlocked by the technology **Reclamation**, which sits on a new row at the bottom of the main tech page and opens once the thermal power plant is researched (on a changed tech tree: whatever unlocks it there). The buildings are in the replicator next to the assembler and the thermal plant. On the build bar they take free keys in the production and power categories; when the power category is full (as with GenesisBook) the incinerator goes beside the converter. A layout saved with Rebind Build Bar is kept.

## Points

Every item is worth points by the raw materials of its cheapest recipe chain, counted over every recipe the loaded game has, mods included: an iron ore or a stone is worth 10 points, and rarer items more. An item no recipe makes or uses is worth 1 point. Each item's description shows its points and the power it gives in an incinerator.

Neither building takes the seven items the converter makes, or anything a pump draws from an ocean (water, sulfuric acid).

## Trash Converter

An assembler whose input is points. Pick what it makes in its recipe list, which shows only these items; the points gather as the recipe's input and each time they reach the price, one item is made. An item worth 50 points takes 20 000 of itself for a Core Element.

| Item | Price (points) |
|---|---|
| Energy Shard | 10 000 |
| Dark Fog Matrix | 50 000 |
| Silicon-based Neuron | 100 000 |
| Matter Recombinator | 200 000 |
| Negentropy Singularity | 500 000 |
| Core Element | 1 000 000 |
| Dark Fog Graviton Lens | 2 000 000 |

A fuel counts for at most half its heat value here. The prices are far above what anything made from these items burns for. Changing the recipe or taking the converter down gives the waiting points back as a Points item, which only a converter takes.

## Incinerator

A thermal power plant three times as strong: 6.67 MW at full load, at the thermal plant's 80% efficiency.

- **Fuels** burn for their own heat value, as in a power plant.
- **Anything else** gives energy by its points, and the more it is worth the more each point gives: a raw material (10 points) gives 80 kJ of power, an item worth 100 points 2.5 MJ, and from 1000 points on every point gives 80 kJ.
- **Rare items also burn at a higher power:** from 32 points on, +25% per level, two levels for every ten times the points, up to +250%.

What waits inside is Heat (100 kJ of fuel each). Sorters stop feeding while 1000 Heat wait. Taking the incinerator down gives the Heat back as an item, which only an incinerator burns.

## Feeding

- **Sorters** take any burnable item from a belt, a storage, a logistics station, an assembler, a lab, an EM-rail ejector or a silo.
- **By hand:** click the input slot with items in hand, or Shift/Ctrl-click a stack in your inventory while the window is open. As many as fit go in; the rest stays in your hand.
- **Fill key (Tab):** one item at a time, the one in hand or else the last burnable one in your inventory; hold it to keep feeding until the building is full.

Spray on burned items is lost.

## Windows

- The input slot and the tip over the building show the item burned last.
- **Burned** opens a list of everything the building has burned since the game was loaded: how many of each, and the points or power they gave. It scrolls.
- The converter shows the points it holds and the points still missing for the next item.
- The incinerator shows the power of the item burned last, its power bonus, and the stock that waits with the time it lasts at full load.

## Compatibility

- Works with GenesisBook, MoreMegaStructure, They Come From Void, DSPCalculator and the BUG mods; item points follow whatever recipes those mods add or change.
- Replicator cells and build-bar keys are picked when the game starts, among those no other mod uses.
- Statistics count burned items as consumed and the converter's items as produced.

## Settings

`BepInEx/config/bug.dsp.trashstation.cfg`, applied after restarting the game:

| Setting | Default | Meaning |
|---|---|---|
| `Language` | English | Language of the mod's own texts: English, Chinese or Turkish |
| `MaxPointsPerMinute` | 5000 | The incinerator's full load, 1620 to 100000 (5000 is 6.67 MW); built incinerators follow it |
| `ListX`, `ListUp`, `ListWidth`, `ListHeight` | 170, 25, 525, 147 | Place and size of the burned list in the converter window, within fixed limits |

## Building from source

You need a current .NET SDK and the .NET Framework 4.7.2 targeting pack. The game, Unity and BepInEx assemblies are referenced from your own installation and are not part of this repository.

```text
dotnet build -c Release "-p:DSPGamePath=D:\Games\Dyson Sphere Program" "-p:BepInExCorePath=C:\path\to\BepInEx\core"
```

The output is `bin/Release/net472/BUG_Reclamation.dll`. Copy it, with the model files built next to it (`*.tsm`, `*.png`), to `BepInEx/plugins/BUG_Reclamation/`.

## License

MIT, see [LICENSE](LICENSE).
