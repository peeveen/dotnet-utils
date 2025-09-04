using Peeveen.Utils.Array;

namespace Peeveen.Util.Test.Array;

public class ArrayExtensionsTests {
	[Fact]
	public void TestMultidimensionalArrayConverterFailsWithJaggedArray() {
		var jaggedArray = new int[][][]{
			[
				[1,2,3],
				[4,5,6],
			],
			[
				[7,8,9],
				[10,11],
			]
		};
		Assert.Throws<InvalidOperationException>(() => jaggedArray.ToMultidimensionalArray<int>());
	}
	
	[Fact]
	public void TestMultidimensionalArrayConverterSucceedsWithNonJaggedArray() {
		var nonJaggedArray = new int[][][]{
			[
				[1,2,3],
				[4,5,6],
			],
			[
				[7,8,9],
				[10,11,12],
			]
		};
		var multidimensional = nonJaggedArray.ToMultidimensionalArray<int>();
		Assert.NotNull(multidimensional);
		Assert.Equal(3, multidimensional.Rank);
		Assert.Equal(12, multidimensional.GetValue(1, 1, 2));
	}

	[Fact]
	public void TestMultidimensionalArrayConverterSucceedsWithEmptyArray() {
		var emptyArray = System.Array.Empty<int>();
		var multidimensionalWithoutDimensionArgument = emptyArray.ToMultidimensionalArray<int>();
		Assert.NotNull(multidimensionalWithoutDimensionArgument);
		Assert.Equal(1, multidimensionalWithoutDimensionArgument.Rank);
		var requiredDimensions = 4;
		var multidimensionalWithDimensionArgument = emptyArray.ToMultidimensionalArray<int>(requiredDimensions);
		Assert.NotNull(multidimensionalWithDimensionArgument);
		Assert.Equal(requiredDimensions, multidimensionalWithDimensionArgument.Rank);
	}
}
