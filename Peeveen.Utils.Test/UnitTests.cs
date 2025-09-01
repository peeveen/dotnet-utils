using FluentAssertions;
namespace Peeveen.Utils.Test {
	public class UnitTests {
		[Fact]
		public void Test1() {
			var result = true;
			result.Should().Be(true);
		}
	}
}
