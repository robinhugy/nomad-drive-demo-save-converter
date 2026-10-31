# 2.0.1

By **robinhugy**

- Renamed the copy button to **backup and create copies**.
- Added a visible four-step guide and checked-world count.
- **Set action** reports how many checked worlds received an action and explains that it does not copy files.
- Added a clear message when no checkbox is selected. Highlighting a row alone never includes it in an operation.
- Explained that copying becomes available only after a successful **Preview selected** operation; preview errors keep copying disabled.
- Repeating **Set action** with unchanged settings preserves an already successful preview.
- Actions, previews, backups and copies operate only on checked worlds. An unreadable unchecked save does not block a selected operation.
- Added 18 Windows Forms checks covering the workflow, selection isolation, read-only previews, backup creation and unchanged source saves.

Demo-to-Early-Access conversion, the 1–8 player limits, save-format support and target-game compatibility are unchanged from 2.0.0. This release remains one standalone English-language .NET Framework EXE.

To copy worlds: check their **Select** boxes, choose **Copy to Early Access**, click **Set action** if needed, then **Preview selected**, then **backup and create copies**. Originals remain unchanged; results have the **(converted)** suffix.
