using System;
using System.Collections.Generic;
using System.Linq;

namespace Peeveen.Utils.Array {
	/// <summary>
	/// Extension functions for arrays.
	/// </summary>
	public static class ArrayExtensions {
		private static List<int> GetDimensionSizes(System.Array array) {
			var length = array.Length;
			var sizes = new List<int> { length };
			if (length > 0) {
				var allArrays = true;
				foreach (var arrayItem in array)
					allArrays &= arrayItem?.GetType().IsArray ?? false;
				if (allArrays) {
					var innerSizes = new IReadOnlyList<int>[array.Length];
					for (int f = 0; f < innerSizes.Length; ++f)
						innerSizes[f] = GetDimensionSizes((System.Array)array.GetValue(f));
					var innerSizeStrings = innerSizes.Select(s => string.Join("_", s));
					if (innerSizeStrings.Distinct().Count() == 1)
						sizes.AddRange(innerSizes[0]);
					else
						sizes.Add(-1);
				}
			}
			return sizes;
		}

		private static void PopulateArray(System.Array multi, System.Array source, int dimensions, int[] dim) {
			var thisDim = dim.Length;
			int[] newDim = new int[thisDim + 1];
			System.Array.Copy(dim, newDim, dim.Length);
			var setting = newDim.Length == dimensions;
			for (int f = 0; f < source.Length; ++f) {
				newDim[thisDim] = f;
				if (setting)
					multi.SetValue(source.GetValue(f), newDim);
				else
					PopulateArray(multi, (System.Array)source.GetValue(f), dimensions, newDim);
			}
		}

		/// <summary>
		/// Converts the given array of arrays (of arrays, etc) to a multidimensional array. Will throw an
		/// exception if all dimensions of the given array are not equal.
		/// </summary>
		/// <typeparam name="T">Type of value contained in the array.</typeparam>
		/// <param name="array">The array to convert.</param>
		/// <param name="requiredDimensions">Number of dimensions in required array. This can USUALLY be determined
		/// by examining the contents of the source array. HOWEVER, if the source array is EMPTY, then we have
		/// no way of telling, so this value will be relied upon.</param>
		/// <returns>The array as a multidimensional array.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the array contains unequal dimensions.</exception>
		public static System.Array ToMultidimensionalArray<T>(this System.Array array, int requiredDimensions = 0) {
			if (array == null || array.Rank > 1) return array;
			var sizes = array.Length == 0 && requiredDimensions != 0 ? new int[requiredDimensions] : GetDimensionSizes(array).ToArray();
			var jagged = System.Array.Exists(sizes, size => size == -1);
			if (jagged) throw new InvalidOperationException("Cannot convert a jagged array with unequal dimensions to a multi-dimensional array.");
			var multi = System.Array.CreateInstance(typeof(T), sizes);
			var dimensions = sizes.Length;
			PopulateArray(multi, array, dimensions, System.Array.Empty<int>());
			return multi;
		}
	}
}