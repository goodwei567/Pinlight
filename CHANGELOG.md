# Changelog

## 0.3.2

- Reduced scrolling-capture sampling from 160 ms to about 30 ms.
- Removed the requirement for two identical frames before stitching.
- Accept smaller frame overlaps during fast continuous scrolling.
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
