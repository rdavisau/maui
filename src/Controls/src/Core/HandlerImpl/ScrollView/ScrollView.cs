﻿#nullable disable
using System;

namespace Microsoft.Maui.Controls
{
	public partial class ScrollView
	{
		[Obsolete("Use ScrollViewHandler.Mapper instead.")]
		public static IPropertyMapper<IScrollView, ScrollViewHandler> ControlsScrollViewMapper =
				new PropertyMapper<ScrollView, ScrollViewHandler>(ScrollViewHandler.Mapper)
				{
#if IOS
					[PlatformConfiguration.iOSSpecific.ScrollView.ShouldDelayContentTouchesProperty.PropertyName] = MapShouldDelayContentTouches,
#endif
				};

		internal static new void RemapForControls()
		{
			// Adjust the mappings to preserve Controls.ScrollView legacy behaviors
#if IOS
			ScrollViewHandler.Mapper.ModifyMappingWhen<ScrollView, IScrollViewHandler>(PlatformConfiguration.iOSSpecific.ScrollView.ShouldDelayContentTouchesProperty.PropertyName, MapShouldDelayContentTouches);
#endif
		}
	}
}
