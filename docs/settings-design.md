# Settings design contract

RoundedTB changes the shape and visibility of the Windows taskbar. It can use one continuous bar or separate Apps, Widgets, and system tray areas. It does not replace Windows app icons, menus, or notification banners. Widgets depend on Windows providing their button.

The settings form one continuous page. Section links move the scroll rather than replacing content:

1. Layout & visibility: choose each area's visibility.
2. Shape & hover: edit the selected area's geometry and reveal behavior.
3. App activity: reveal auto-hidden Apps for new windows or flashing icons.
4. Windows integration: restoration events and sign-in startup.
5. Advanced: diagnostic tests and the unstable maximized-window screen-space experiment.

Always visible and Auto-hide are available for every active area. Widgets and tray also support Hidden. Hidden is different from Auto-hide: hovering does not reveal it. Full Windows taskbar restoration can temporarily display hidden areas. With the screen-space experiment, restoration reveals enabled rounded areas and leaves Hidden areas hidden.

Continuous layout scopes shape, hover, and activity reveal to the entire bar; separate-area preferences are retained. Both editing cards name the selected area. Bottom handles apply only to separate Apps and tray. Dependent controls remain visible with an explanation when unavailable.

All settings take effect immediately and save automatically. The fixed top-right control shows Restore Taskbar while customization is active and Enable RoundedTB while Windows defaults are active. Restoration preserves preferences; enabling resumes rounded areas. The old layout on/off card and Apply footer are removed.

Designer review and prospective-user feedback informed the hierarchy, visibility vocabulary, explicit editing scope, and save behavior. Headless UI tests cover continuous navigation, visibility modes, preference preservation, and pending changes. Native navigation tests verify the installed Windows interface.

Each area now has independent visibility rows for normal and maximized windows. Existing visibility initializes both modes. Maximized state is detected per monitor, excluding the window being dragged; hover geometry and effects are shared between modes. Full-taskbar restoration events take priority over these choices.
