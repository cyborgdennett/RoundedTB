using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoundedTB
{
    public class Types
    {
        public class Taskbar
        {
            public IntPtr TaskbarHwnd { get; set; } // Handle to the taskbar
            public IntPtr TrayHwnd { get; set; } // Handle to the tray on the taskbar (if present)
            public IntPtr AppListHwnd { get; set; } // Handle to the list of open/pinned apps on the taskbar
            public LocalPInvoke.RECT TaskbarRect { get; set; } // Bounding box for the taskbar
            public LocalPInvoke.RECT TrayRect { get; set; }  // Bounding box for the tray (dynamic)
            public LocalPInvoke.RECT AppListRect { get; set; } // Bounding box for the list of pinned & open apps (dynamic)
            public LocalPInvoke.RECT WidgetsRect { get; set; }
            public bool HasWidgets => WidgetsRect.Right > WidgetsRect.Left && WidgetsRect.Bottom > WidgetsRect.Top;
            public bool AppearanceRestored { get; set; }
            internal bool RevealAllSegments { get; set; }
            internal bool HasMaximizedWindow { get; set; }
            public bool HasMeasuredAppListBounds { get; set; }
            public IntPtr RecoveryHrgn { get; set; } // Pointer to the recovery region for any given taskbar. Defaults to IntPtr.Zero
            public double ScaleFactor { get; set; } // The scale factor of the monitor the taskbar is on
            public string TaskbarRes { get; set; } // Resolution of the taskbar as text
            public bool Ignored { get; set; } // Specifies if the taskbar should be ignored when applying changes
            public bool TaskbarHidden { get; set; } // Specifies if this taskbar is currently hidden by RTB
            internal long? AppOpenRevealStartedAt { get; set; }
            internal bool AppAttentionRevealActive { get; set; }
            internal HoverTransition AppHoverTransition { get; } = new();
            internal HoverTransition TrayHoverTransition { get; } = new();
            internal HoverTransition WidgetsHoverTransition { get; } = new();
            public bool TrayPopupOpen { get; set; }
            public bool WindowPreviewOpen { get; set; }
            public bool StartMenuOpen { get; set; }
            public bool AppListHovered { get; set; }
            public bool TrayHovered { get; set; }
            public bool WidgetsHovered { get; set; }
            public bool TrayHidden { get; set; } // Specifies if the tray is currently hidden by RTB on this taskbar
            public int AppListWidth { get; set; } // Specifies the width of the app list
            public TaskbarEffect TaskbarEffectWindow { get; set; } // Unused clone to apply effects to the taskbar
        }

        public class Settings
        {
            internal bool VisibilityResolved { get; set; }
            internal Settings Copy() => (Settings)MemberwiseClone();
            public int Version {  get; set; }
            public SegmentSettings SimpleTaskbarLayout { get; set; }
            public SegmentSettings DynamicAppListLayout { get; set; }
            public SegmentSettings DynamicTrayLayout { get; set; }
            public SegmentSettings DynamicWidgetsLayout { get; set; }
            public bool IsWindowsDefault { get; set; }
            public bool IsDynamic { get; set; }
            public bool IsCentred { get; set; }
            public bool IsWindows11 { get; set; }
            public bool ShowTray { get; set; }
            public bool ShowWidgets { get; set; }
            public bool IsNotFirstLaunch { get; set; }
            public bool FillOnMaximise { get; set; }
            public bool FillOnTaskSwitch {  get; set; }
            public bool ShowSegmentsOnHover { get; set; }
            public int AutoHide { get; set; } // Read legacy configurations only.
            public bool ShouldSerializeAutoHide() => false;
            public bool RevealOnAppOpen { get; set; }
            public double AppOpenRevealSeconds { get; set; } = 1;
            public bool RevealOnAppAttention { get; set; }
            public bool KeepAppAttentionOpenUntilAccepted { get; set; }
            public double AppAttentionRevealSeconds { get; set; } = 5;
            public bool ExpandMaximizedWorkArea { get; set; }
        }

        public class EffectiveRegion
        {
            public int CornerRadius { get; set; }
            public int Top { get; set; }
            public int Left { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        public enum VisibilityMode { AlwaysVisible, AutoHide, Hidden }

        public enum HoverAnimationEffect { Slide = 0, Expand = 1, None = 2 }

        public class SegmentSettings
        {
            internal SegmentSettings Copy() => (SegmentSettings)MemberwiseClone();
            public VisibilityMode? MaximizedVisibility { get; set; }
            public bool ShowOnHover { get; set; }
            public bool HoverActivationPill { get; set; }
            public int HoverPillWidth { get; set; } = 80;
            public int HoverPillThickness { get; set; } = 3;
            public double HoverHideDelaySeconds { get; set; }
            [Newtonsoft.Json.JsonProperty("HoverAnimationEnabled", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            private bool? LegacyHoverAnimationEnabled { get; set; }

            [System.Runtime.Serialization.OnDeserialized]
            internal void MigrateAnimation(System.Runtime.Serialization.StreamingContext context)
            {
                if (LegacyHoverAnimationEnabled == false)
                    HoverOpenEffect = HoverCloseEffect = HoverAnimationEffect.None;
                else if (LegacyHoverAnimationEnabled == true)
                {
                    if (HoverOpenEffect == HoverAnimationEffect.None) HoverOpenEffect = HoverAnimationEffect.Slide;
                    if (HoverCloseEffect == HoverAnimationEffect.None) HoverCloseEffect = HoverAnimationEffect.Slide;
                }
                LegacyHoverAnimationEnabled = null;
            }
            public int HoverOpenDurationMs { get; set; } = 180;
            public int HoverCloseDurationMs { get; set; } = 140;
            public HoverAnimationEffect HoverOpenEffect { get; set; } = HoverAnimationEffect.None;
            public HoverAnimationEffect HoverCloseEffect { get; set; } = HoverAnimationEffect.None;
            public int CornerRadius { get; set; }
            public int MarginTop { get; set; }
            public int MarginLeft { get; set; }
            public int MarginBottom { get; set; }
            public int MarginRight { get; set; }
        }

        public enum TrayMode
        {
            Show = 0,
            Hide = 1,
            AutoHide = 2,
        }

        public enum KeyModifier
        {
            None = 0,
            Alt = 1,
            Control = 2,
            Shift = 4,
            WinKey = 8
        }
    }
}
