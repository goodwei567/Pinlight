# Changelog

## 0.3.9

- Added a **Start with Windows (tray)** option under Settings.
- The option uses a per-user startup entry and does not require administrator privileges.
- Disabling it removes the startup entry immediately.
- Existing startup entries are updated to the currently running build after an upgrade.

## 0.3.8

- Refined the capture selection with layered borders, rounded resize handles, and a content-sized dimension label.
- Added spacing, separators, rounded controls, and consistent hover, pressed, and selected states to the capture toolbars.
- Preserved the existing button order, actions, and dark gray/blue visual language.

## 0.3.7

- Completed the main window's first layout and paint off-screen before moving it into view.
- Reduced transient white or partially rendered frames during startup.

## 0.3.6

- Completed layout and first-frame painting before displaying the main window.

## 0.3.5

- Added 3, 5, and 10 second choices to the delayed-capture button.

## 0.3.4

- Removed the permanently highlighted capture button state.
- Unified hover, pressed, and selected button styling.
- Reduced repeated redraws and button response flicker.

## 0.3.3

- Added a setting to hide the Pinlight window while capturing.
- The main window can be included in captures when the option is disabled.

## 0.3.2

- Reduced scrolling-capture sampling from 160 ms to about 30 ms.
- Removed the requirement for two identical frames before stitching.
- Accepted smaller frame overlaps during fast continuous scrolling.
- Added pre-sampled frame matching to reduce CPU time per frame.
- Added an incremental thumbnail cache for the live side preview.
- Kept PNG export and clipboard output at original screen resolution.
- Added regression coverage for rapid scrolling, narrow overlap, immediate preview updates, and performance.

## 0.3.1

- Added a live side preview to scrolling capture.
- Positioned the preview on the right or left according to available space.
- Excluded the preview from captured pixels and added a hide fallback.

## 0.3.0

- Added downward scrolling capture with pause, resume, copy, save, and pin actions.
- Restored the desktop input path while capturing.
- Added overlap matching, ambiguity rejection, and long-image size limits.

## 0.2.0

- Introduced the compact dark interface and simplified workspace.
- Added custom global shortcuts, history, favorites, and persistent pins.
- Improved local persistence for encrypted or redirected folders.

## 0.1.0

- Initial Windows screenshot and pin utility.
