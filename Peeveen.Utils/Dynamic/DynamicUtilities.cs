using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;

namespace Peeveen.Utils.Dynamic {
	/// <summary>
	/// Describes a dynamic property.
	/// </summary>
	public class DynamicPropertyInfo {
		/// <summary>
		/// Constructor.
		/// </summary>
		/// <param name="Name">Name</param>
		/// <param name="Type">Type</param>
		public DynamicPropertyInfo(string Name, Type Type) {
			this.Name = Name;
			this.Type = Type;
		}
		/// <summary>
		/// Property name
		/// </summary>
		public string Name { get; }
		/// <summary>
		/// Property type
		/// </summary>
		public Type Type { get; }
	}

	/// <summary>
	/// Utilities for working with dynamic data.
	/// </summary>
	public static class DynamicUtilities {
		private static readonly char[] ExpressionSeparators = new char[] { '.', '[', ']', '"' };

		private static object GetDictionaryItem(dynamic obj, string key) {
			if (obj is IReadOnlyDictionary<string, object> readOnlyDict)
				return readOnlyDict[key];
			if (obj is IDictionary<string, object> dict)
				return dict[key];
			var type = ((object)obj).GetType();
			var value = type.GetProperty(key)?.GetValue(obj);
			return value;
		}

		private static object GetListItem(dynamic obj, int index) {
			if (obj is IReadOnlyList<object> readOnlyList)
				return readOnlyList[index];
			if (obj is IList<object> list)
				return list[index];
			return ((Array)obj).GetValue(index);
		}

		private static T EvaluateExpression<T>(dynamic obj, string[] parts, int partIndex = 0) {
			var part = parts[partIndex];
			dynamic result;
			if (uint.TryParse(part, out var index))
				result = GetListItem(obj, (int)index);
			else
				result = GetDictionaryItem(obj, part);
			++partIndex;
			if (parts.Length > partIndex && result != null)
				// Don't be fooled by the IDE here. You NEED THIS TYPE DECLARATION.
				return EvaluateExpression<T>(result, parts, partIndex);
			return (T)result;
		}

		/// <summary>
		/// Evaluates an expression against a dynamic object.
		/// </summary>
		/// <typeparam name="T">Expected return type.</typeparam>
		/// <param name="obj">Object to evaluate the expression against.</param>
		/// <param name="expression">Expression to evaluate.</param>
		/// <returns></returns>
		public static T EvaluateExpression<T>(dynamic obj, string expression) =>
			EvaluateExpression<T>(obj, expression.Split(ExpressionSeparators, StringSplitOptions.RemoveEmptyEntries));

		/// <summary>
		/// Examines the given object, and flattens any nested structures.
		/// Nested values will be extracted and placed at the root level with
		/// a property name combined from the parent properties.
		/// </summary>
		/// <param name="obj">Dynamic object to examine.</param>
		/// <param name="separator">Separator to use for combining property names.</param>
		/// <param name="includeCollections">True to include collections (lists, arrays, etc).
		/// If true, flattened values will end up as properties called CollectionProperty_n,
		/// where n is the collection index. If false, collections remain unflattened in
		/// the result object.</param>
		/// <returns></returns>
		public static dynamic Flatten(dynamic obj, string separator = "_", bool includeCollections = false) {
			IDictionary<string, object> result = new ExpandoObject();
			string CombinePropertyNames(string prefix, string propName) =>
				string.IsNullOrEmpty(prefix) ? propName : prefix + separator + propName;
			void RecurseEnumerable(IEnumerable enumerable, string currentPrefix) {
				int index = -1;
				foreach (var item in enumerable) {
					++index;
					var newPrefix = CombinePropertyNames(currentPrefix, index.ToString());
					Recurse(item, newPrefix);
				}
			}
			void Recurse(dynamic currentObj, string currentPrefix) {
				if (currentObj is ValueType || currentObj is string)
					// Just a value.
					result[currentPrefix] = currentObj;
				else if (currentObj is IDictionary<string, object> dict) // Includes ExpandoObject
					foreach (var kvp in dict) {
						var newPrefix = CombinePropertyNames(currentPrefix, kvp.Key);
						Recurse(kvp.Value, newPrefix);
					}
				else if (currentObj is IEnumerable enumerable)
					if (includeCollections)
						RecurseEnumerable(enumerable, currentPrefix);
					else
						result[currentPrefix] = currentObj;
				else {
					// Some other object. Use reflection to get its public properties.
					var type = (currentObj as object).GetType();
					var properties = type.GetProperties();
					foreach (var prop in properties) {
						var newPrefix = CombinePropertyNames(currentPrefix, prop.Name);
						Recurse(prop.GetValue(currentObj), newPrefix);
					}
				}
			}
			Recurse(obj, string.Empty);
			return result;
		}

		/// <summary>
		/// Returns information about the discoverable properties of the given dynamic object.
		/// </summary>
		/// <param name="obj">Dynamic object</param>
		/// <returns>Property info</returns>
		public static IEnumerable<DynamicPropertyInfo> GetPropertyInfo(dynamic obj) {
			if (obj is IDictionary<string, object> expandoDict)
				foreach (var kvp in expandoDict)
					yield return new DynamicPropertyInfo(kvp.Key, kvp.Value.GetType());
			else {
				var type = obj.GetType();
				var properties = type.GetProperties();
				foreach (var prop in properties)
					yield return new DynamicPropertyInfo(prop.Name, prop.PropertyType);
			}
		}
	}
}