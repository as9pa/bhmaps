Facts gathered 2026-10-14 from the game data and by observing Azu Modification (unlicensed; no code copied).

# Brawlhalla colour schemes and skin recolouring

## 1 Summary

- The game defines every legend colour scheme as one record in an XML file inside Game.swz, named ColorSchemeTypes. Each record lists up to 34 swap colours (hex RGB), grouped by body part and shade.
- Sprites in the game are drawn in fixed "key" colours. At runtime the game replaces each key colour with the colour the scheme gives for that slot (inferred). The scheme is therefore a lookup table, not a tint.
- The reference app (Azu Modification) does two separate things. (a) It edits colour schemes and previews them on exported sprite SVGs in a web UI. (b) To make a scheme appear in game, it rewrites one ActionScript method body inside UI_MainMenu.swf, using the external tool FFDec, so a chosen existing scheme slot gets new colour numbers. It keeps a .bak copy of the SWF.
- Nothing in the reference app changes ColorSchemeTypes.xml or the swz archives. Installed colours live in the SWF bytecode.

## 2 Where the game keeps colour schemes

Confirmed by decrypting with BhMaps.Core (SwfKeyFinder + SwzReader; key found this session: 235141011; it rotates with updates).

- File: Game.swz, one entry with root element ColorSchemeTypes (about 146 KB of XML). It is the only entry with ColorScheme in its name across Game, Init, Dynamic and Engine swz.
- 81 ColorSchemeType records. The first is a documentation record named Template (every value is "--"). ColorSchemeID values run up to 144, with gaps.
- Attribute: ColorSchemeName (internal name, for example Red, TeamRed1).
- Child elements per record:

| Element | Meaning (observed) |
|---|---|
| ColorSchemeID | integer id |
| DefaultUnlocked | TRUE / FALSE / false / "--" (mixed case in data) |
| DisplayNameKey | key into the language string table (ColorSchemeType_NAME_DisplayName) |
| IconName, IconFileName | icon symbol and the SWF that holds it (UI_Icons) |
| UniversalIcon*, InfinityIcon*, UniversalIconOriginalDimension | extra icons, only on some records |
| OrderID | sort order in the picker |
| Rarity | R (71 records), M (9), None (1) |
| TeamColor | 0 for normal schemes (59), 1 to 4 for team colour slots (4 records each), one record has the text False |
| BroadcasterEnabled | true/false |
| 34 swap colours | see section 3; written as 0xRRGGBB |
| ExcludeOpponentTeamColor | comma list of scheme names that must not be used against this one |
| FallbackOpponentTeamColor, FallbackMyTeamColor | scheme names to use on a clash |
| IndicatorColor | single 0xRRGGBB used for the player indicator |

- Example (Red, ID 1, abridged by me): Hair 0x600D09, Body 1 base 0xCF1F31, Body 1 very dark 0x560222, Weapon base 0x807474; indicator 0xCF1F31; fallback opponent TeamBlue1; fallback own team TeamRed1. Team records (for example TeamRed1) have TeamColor 1 and the same Body 1 base.
- Hand fields: no record has a 0x value for HandsLt_Swap (checked by count), so the four Hands slots appear unused.

## 3 Colour groups and shades

Shade suffixes: VL very light, Lt light, (none) base, Dk dark, VD very dark, Acc accent.

| Group | Shades present |
|---|---|
| Hair | Lt, base, Dk |
| Body 1 | VL, Lt, base, Dk, VD, Acc |
| Body 2 | VL, Lt, base, Dk, VD, Acc |
| Special | VL, Lt, base, Dk, VD, Acc |
| Hands | Lt, Dk, SkinLt, SkinDk (empty in data) |
| Cloth | VL, Lt, base, Dk |
| Weapon | VL, Lt, base, Dk, Acc |

That is 3+6+6+6+4+4+5 = 34 slots in the XML. The reference app builds a 35-entry array indexed 1 to 34 (index 0 unused). Order: Hair 1-3, Body 1 4-9, Body 2 10-15, Special 16-21, Hands 22-25 (left 0), Cloth 26-29, Weapon 30-34. The Hands range is inferred from the numbering gap.

## 4 How recolouring a sprite works

- In the game (inferred from the data shape, not verified in game code): each slot has a fixed key colour in the SWF art; the engine swaps it for the scheme colour. A 34-entry table implies exact swapping, not blending.
- In the reference app (observed from UI and file layout, not read in depth): sprites are SVG exports of SWF shapes. The PreRenders folder holds 63 black-fill silhouettes; real previews come from uploaded SVG, a zip of SVGs, or SWF files. Colours found in the art are mapped to palette slots through a "sprite type" definition (XML files the user can add), then replaced by the current palette. The user can mark shapes or pixel regions as "skin" so they keep original colours. Other sprite types exist (the UI mentions a 36-colour layout for effects such as dash and gravity cancel).
- Not confirmed: whether matching is exact or nearest-colour.

## 5 Game files touched and how

- Target: UI_MainMenu.swf in the Brawlhalla folder (found via Steam library paths or drive scan). Installing does not read ColorSchemeTypes.xml.
- Mechanism (observed): the app generates ActionScript bytecode text (pcode) for one method body. It looks up a game class by name, reads a static vector of scheme objects, picks entries by index (a number such as 32 or 33, i.e. an existing scheme slot, which it overwrites) and assigns each a new array of 35 unsigned ints. Team colours use a second generated block. The class, vector and array names are obfuscated and change each game update; the app has a discovery and "relink" flow that re-derives them from BrawlhallaAir.swf.
- FFDec operations: export a class script with method body ids shown (to get the body index), export a class pcode, and replace one method body from a pcode file. Output goes to a temp SWF next to the original, then moves over it.
- Safety: first install copies the SWF to the same name plus .bak (only if absent). "Reset all" copies the .bak back. Installed colours are also kept in a JSON file beside the app so the set can be re-injected after a patch or removal. A GameBanana-build mode installs into a separate SWF copy.
- SWF tags: the patched tag is DoABC (method body in the ABC data). A bulk "inject shapes" feature also replaces shapes in an SWF; exact tag types not confirmed.

## 6 What the reference app does on top

- Palette editor: grid of groups by shades, undo/redo, row locks, auto-shade from base, hue and brightness steps, desaturate, invert, gradient between two cells, randomise, colour-blindness correction, sampling from a template image.
- Import: Paste code box accepting XML, JSON, INI (.wcolor) and pcode text; file drop for svg/json/xml/ini/txt/palette/wcolor.
- Export: XML (the game record format), .wcolor INI, share link, share image PNG, copy code.
- Sprite preview with zoom and animation, copy as PNG, recoloured SVG download, zip batch, per-shape SWF export and injection.
- Install and uninstall one scheme into a chosen slot, list installed ones, reset all from backup, team colour slots.
- Also: auto-update and sync services, org palettes, a dev-mode unlock.

## 7 Open questions for our own build

1. Does the game compare key colours exactly or by nearest? Settle by listing fill colours in a Gfx SWF and checking they cluster on 34 values.
2. Are the Hands slots really unused? Check hand key colours in a few Gfx SWFs.
3. Which SWF tags must we parse for preview? Minimum: DefineShape 1 to 4 (solid fills), DefineSprite and PlaceObject for composition, symbol class names.
4. Do we write to the game at all? Options: only edit and export ColorSchemeTypes-style XML (safe), or patch UI_MainMenu.swf bytecode (needs an ABC reader/writer, the obfuscated names, per-update relinking; fragile).
5. Where does the game apply the swap (shader or CPU)? Not determined.
6. Could ColorSchemeTypes in Game.swz be replaced by repacking the swz? Key and PRNG are known and a writer exists in tests; untested, and checksums may block it.
