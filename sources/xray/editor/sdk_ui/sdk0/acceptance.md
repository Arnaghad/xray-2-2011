# SDK-0: завершення inventory

Дата: 2026-10-24. **SDK-0 виконано як етап інвентаризації оригіналу.**
Єдиний checklist — `sdk_migration_plan.md`. Повний machine-readable baseline —
`function-matrix.json`, поле `inventoryAcceptance`; implementation stages
SDK-1–SDK-12 мають власне приймання працездатності.

## Межі прийнятого baseline

Еталон: `tsnest/xray-2-2011`, commit
`1795aa33a51fdde2ff5ea10627fc6d0b2b755c56`. Для 36 обов'язкових source-backed
workflow groups зафіксовано джерела, original project entries, current native/
managed build membership, залежності, сценарій перевірки та наступний SDK stage.
921 source references можуть повторюватися між групами.

Прямі події та command registrations розширюють ці групи без прихованого
відкидання джерел. Для всіх 305 command candidates є ім'я, shortcut, registration
body, action initializer, predicate source candidates і один із 16 reviewed
construction routes. 51 команда має окремий `set_enabled` binding; решта
використовують власні/успадковані `enabled`/`capture`. Їхні результати та
пріоритети shortcuts потрібно перевірити під час порту відповідних workflows.

Єдина command candidate з вимкненим caller — object painter. Стан і
закоментований caller збережено явно. Scripting tool/chooser існують, але tab
закоментовано. Ці джерела не рекламуються як підтверджені working UI routes.

## Покриття evidence

- 2483 pinned source files, 13 original projects, PE/CLR metadata 45 binaries,
  42 file-format declarations, 12 680 original asset paths/sizes і 9 seed hashes.
- Усі 339 action registration candidates класифіковано; 29 source entries
  поза original project lists і 5 API/forwarding збережено окремо. Зіставлено
  92 menu/toolbar bindings; первинні 85 menu/tool entries також класифіковано.
- 1931 original C++/CLI/C#/header file перевірено за SHA-256/Git blob перед
  прийманням. Каталог має 1371 explicit source/XAML event bindings:
  1096 interaction/data candidates, 82 lifecycle/layout/presentation та
  193 bindings із source entries, не перелічених в original projects.
  Not-listed entry не є доказом compiler/preprocessor exclusion.
- Для 1320 event bindings знайдено по одному source handler candidate,
  23 передають делегати, 3 звертаються до virtual/interface slots, 25 містять
  inline delegates. Нерозібраних цілей і XAML parse errors у цьому каталозі немає.
  25 прив'язаних порожніх bodies збережено як callback/hook facts; concrete
  overrides та effects перевіряються у відповідному порту.
- Переглянуто всі знайдені 126 documentation copies: 18 DOCX/TXT/XLSX copies
  (9 унікальних файлів) та 108 PNG/JPG/PSD copies (54 унікальні stored images/
  PSD composites). Hidden Photoshop layers не трактуються як додаткові UI specs.
  Annotated IDE layout, icons та art references мають explicit provenance.
- Уточнено всі Maya registrations: 9 commands, 1 file translator, 2 nodes,
  3 transforms. Імена звірено з literals/macros pinned sources; це original
  Maya ABI evidence, без твердження про сумісність зі сучасним host.

## Відомі gaps та відповідальність наступних етапів

Gaps є частиною inventory і мають stage owners у `implementationFollowUps`:

- **Original startup:** незалежна workspace-копія завершується з
  `0xC0000142`; exact path/PID event — `xray_engine_pc.dll`, exception
  `0xC0000005`, offset `0x10F820`. Причину не встановлено. Застосовано
  source/documentation fallback; fresh original runtime acceptance відсутнє.
- **Dormant routes:** object painter і scripting tab — SDK-7/SDK-11.
- **Document/source differences:** Tags/filter/visibility, curve presets/3D
  track, mixer/EQ/compressor та human-template UI — SDK-2/4/9/11. Задум із
  документа або іконка не підміняють source-backed working feature.
- **Empty hooks:** перевірка concrete overrides та очікуваного ефекту —
  SDK-2/4/6/8/11.
- **Fixtures/cooks/formats:** missing exact model/clip/WAV, root-only particle,
  linked-dialog coverage, animation/material transitive dependencies —
  SDK-3/6/8/9/10/11/12. Дві static sound closures і два replacement candidates
  знайдено. Жодному fixture не зараховано runtime/cook acceptance.
- **External hosts/exporters:** current Maya port, Excel COM replacement,
  OBJ/Maya/workbook roundtrips та packaged dependencies — SDK-6/11/12.

Ці gaps не приховані за статусом SDK-0: source completeness, dependencies і
способи перевірки відомі; реалізація, asset repair та behavior acceptance
виконуються на визначених implementation stages. Наявність файла, DLL,
події або source predicate не зараховується як виконаний editor workflow.

## Перевірки та повторення

`build/sdk0-ui-audit-20261024.json`: 32 checks, включно з original source/image
hashes, delegate arity, XAML inheritance, typedef adapters, virtual slots,
complete command/route mapping, matrix artifact hashes і negative finalize/
stale-evidence checks та відхилення змінених зображень до нового visual review.
`xray_source_contract` CTest пройшов.
Попередній inventory checkpoint і його 18 integrity checks збережено окремо.

Canonical additions: `build/sdk0-events-20261024-audited/`,
`build/sdk0-images-20261024/`, `build/sdk0-routes-visuals-20261024-complete/`.
Original SDK/assets і старі evidence roots не змінювалися.

```powershell
python sources/tools/sdk/inspect-sdk0-events.py --output build/sdk0-events-new
python sources/tools/sdk/inspect-sdk0-images.py --output build/sdk0-images-new
python sources/tools/sdk/review-sdk0-routes.py --events build/sdk0-events-new --images build/sdk0-images-new --output build/sdk0-routes-new
python sources/tools/sdk/map-sdk0.py build/sdk0-baseline --review build/sdk0-review-20261024-final --fixtures build/sdk0-fixtures-20261024-verified --events build/sdk0-events-new --routes build/sdk0-routes-new --finalize-inventory
```

Image review decisions у `review-sdk0-routes.py` стосуються pinned documentation
corpus, переглянутого в цьому проході. Якщо документація або sources зміняться,
потрібні нові review decisions. Генератор перевіряє commit/input/artifact hashes
та повноту source inventory перед записом фіналізованої матриці.
