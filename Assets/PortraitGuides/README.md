# Original Silverpine portrait guides

These are the original game sprites, extracted from the installed Silverpine
1.7.3 assets, not newly drawn approximations. They are embedded in the EXE.

- `silhouette.png`: `sprite_ui_charshadow`, sharedassets0.assets sprite path ID
  460, referenced by level0 `FakeDialogBox/Root/FakeNPCImageRoot/FakeNPCImage`.
- `rule-of-thirds.png`: `sprite_debug_ruleofthirds`, sharedassets0.assets sprite
  path ID 393, referenced by `FakeDialogBox/Root/RuleOfThirds`.

Extraction: workspace `tools/extract_portrait_guides.py` (Python + the local
UnityPy installation). Scene inspection: `tools/inspect_portrait_layout.py`.
The artwork belongs to the game; it is used only as a reference in this
unofficial editor. No ownership or open-source license for these images is
claimed. See the project's THIRD-PARTY-NOTICES.md.

## Coordinate model

The FakeDialogBox and DialogBox CanvasScaler components use ScaleWithScreenSize,
reference resolution 1436 x 600, MatchWidthOrHeight = 0 (match width). Therefore
the logical canvas width is 1436, and height is 1436 / screen aspect ratio.
The native desktop canvas uniformly fits that canvas inside the preview, with letterboxing.

The stretched Root has sizeDelta (-1280, -624), pivot (0.5, 0.5) and position
(0, 0), making its lower-left corner (640, 312). In bottom-left canvas units:

| Object | Origin/pivot position | Image size | Image pivot |
| --- | --- | --- | --- |
| PlayerImageRoot / FakePlayerImageRoot | (1286.199951, 455.737305) + stored offset | 679 x 1347 | (0.580947638, 0.835785449) |
| NPCImageRoot | (225.5, 449.737305) + (177 - stored X, stored Y) | 679 x 1347 | (0.333045781, 0.831534922) |
| FakeNPCImage silhouette | (134.149940, 601.537308) | 396.785004 x 787.142395 | (0.333045781, 0.831534922) |
| RuleOfThirds | (718, 403.499969) | 1436 x 807 | (0.5, 0.5) |

All three portrait Images preserve aspect ratio using the RectTransform pivot.
The guide Image stretches without preserving aspect. The NPC Image has a 180°
Y rotation, so its image is mirrored; CustomNPCManager independently transforms
the stored X offset into 177 - X. DialogBox applies the same stored scale and Y.
The NPC preview hides the reference silhouette because it occupies that slot.

The fitted bounds include transparent PNG padding, just like the runtime loader's
full-texture Sprite.Create. Guide lines render over the portraits, as in the
scene hierarchy. The background is neutral editor gray, not a recreation of the
game world. Dialogue text/chrome, animation, and modded UI transforms are outside
this preview's scope. The drag gesture is unrestricted to support existing custom
offsets; the base game's drag-only limits are X [-100, 300], Y [-300, 300].
