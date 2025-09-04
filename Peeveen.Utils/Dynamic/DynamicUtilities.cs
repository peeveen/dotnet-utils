using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Text.Json;

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
	/// How arrays should be treated by the Flatten() method.
	/// </summary>
	public enum ArrayFlatteningBehavior {
		/// <summary>
		/// Arrays will not be flattened, and will be included in the
		/// flattened result "as-is".
		/// </summary>
		Ignore,
		/// <summary>
		/// Arrays will be omitted from the flattened result.
		/// </summary>
		Omit,
		/// <summary>
		/// Arrays will be serialized to JSON and included in the flattened result
		/// as a string property.
		/// </summary>
		Jsonify,
		/// <summary>
		/// Each array element will become an individual property called
		/// ParentPropertyName_n, where n is the array index.
		/// </summary>
		Flatten
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
		/// Note that if a calculated property name clashes with an existing property
		/// (e.g. you have a property "x" with a nested property "y" that would become
		/// "x_y", but "x_y" already exists as a property at the level of "x") then
		/// data will be lost (whichever property is encountered first in the collection
		/// of properties will be overwritten by the later one).
		/// </summary>
		/// <param name="obj">Dynamic object to examine.</param>
		/// <param name="separator">Separator to use for combining property names.</param>
		/// <param name="arrayFlatteningBehavior">How to handle arrays/collections.</param>
		/// <param name="jsonSerializerOptions">JSON serialization options to use if
		/// arrayFlatteningBehavior is Jsonify.</param>
		/// <returns></returns>
		public static dynamic Flatten(
			dynamic obj,
			string separator = "_",
			ArrayFlatteningBehavior arrayFlatteningBehavior = ArrayFlatteningBehavior.Ignore,
			JsonSerializerOptions jsonSerializerOptions = null
		) {
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
				else if (currentObj is IEnumerable enumerable) {
					switch (arrayFlatteningBehavior) {
						case ArrayFlatteningBehavior.Flatten:
							RecurseEnumerable(enumerable, currentPrefix);
							break;
						case ArrayFlatteningBehavior.Jsonify:
							result[currentPrefix] = JsonSerializer.Serialize(enumerable, jsonSerializerOptions);
							break;
						case ArrayFlatteningBehavior.Ignore:
							result[currentPrefix] = enumerable;
							break;
						case ArrayFlatteningBehavior.Omit:
							break;
					}
				} else {
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