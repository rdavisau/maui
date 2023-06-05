using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Xunit;

namespace Microsoft.Maui.DeviceTests
{
	[Category(TestCategory.Border)]
	public partial class BorderTests : ControlsHandlerTestBase
	{
		[Fact(DisplayName = "Rounded Rectangle Border occupies correct space")]
		public Task RoundedRectangleBorderLayoutIsCorrect()
		{
			var expected = Colors.Red;

			var container = new Grid();
			container.WidthRequest = 100;
			container.HeightRequest = 100;

			var border = new Border()
			{
				Stroke = Colors.Red,
				StrokeThickness = 1,
				BackgroundColor = Colors.Red,
				HeightRequest = 100,
				WidthRequest = 100
			};

			return AssertColorAtPoint(border, expected, typeof(BorderHandler), 10, 10);
		}

		[Fact(DisplayName = "StrokeThickness does not inset stroke path")]
		public async Task BorderStrokeThicknessDoesNotInsetStrokePath()
		{
			var grid = new Grid()
			{
				ColumnDefinitions = new ColumnDefinitionCollection()
				{
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(GridLength.Star)
				},
				RowDefinitions = new RowDefinitionCollection()
				{
					new RowDefinition(GridLength.Star),
					new RowDefinition(GridLength.Star)
				},
				BackgroundColor = Colors.White
			};

			var border = new Border()
			{
				Stroke = Colors.Black,
				StrokeThickness = 10,
				BackgroundColor = Colors.Red
			};

			grid.Add(border, 0, 0);
			grid.WidthRequest = 200;
			grid.HeightRequest = 200;

			await CreateHandlerAsync<BorderHandler>(border);
			await CreateHandlerAsync<LayoutHandler>(grid);

			await AssertColorAtPoint(grid, Colors.Black, typeof(LayoutHandler), 2, 2);
			await AssertColorAtPoint(grid, Colors.Red, typeof(LayoutHandler), 13, 13);
		}
	}
}
