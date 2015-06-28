using System;
using System.Collections.Generic;

namespace SimpleContainer.Helpers
{
	internal static class Utils
	{
		public static T[][] CartesianProduct<T>(this T[][] source)
		{
			if (source.Length == 0)
				return source;
			var resultLength = 1;
			foreach (var item in source)
				resultLength *= item.Length;
			var result = new T[resultLength][];
			for (var i = 0; i < resultLength; i++)
				result[i] = new T[source.Length];
			var resultIndex = 0;
			CartesianIteration(source, result, 0, ref resultIndex);
			return result;
		}

		public static bool SameAs(this Type[] a, Type[] b, int countOfItemsToCompare)
		{
			if (a.Length != b.Length)
				return false;
			for (var i = 0; i < countOfItemsToCompare; i++)
				if (a[i] != b[i])
					return false;
			return true;
		}

		public static int GetSubsequenceLastIndex<T>(this List<T> sequence, List<T> other, IEqualityComparer<T> comparer)
		{
			int i = 0, j = 0;
			while (true)
			{
				if (i >= sequence.Count)
					return j;
				if (j >= other.Count)
					return -1;
				if (comparer.Equals(sequence[i], other[j]))
					i++;
				j++;
			}
		}

		public static List<T> Concat<T>(this List<T> first, List<T> second)
		{
			if (second.Count == 0)
				return first;
			if (first.Count == 0)
				return second;
			var result = new List<T>(first);
			result.AddRange(second);
			return result;
		}

		private static void CartesianIteration<T>(T[][] source, T[][] result, int index, ref int resultIndex)
		{
			foreach (var t in source[index])
			{
				result[resultIndex][index] = t;
				if (index < source.Length - 1)
					CartesianIteration(source, result, index + 1, ref resultIndex);
				else
				{
					resultIndex++;
					if (resultIndex < result.Length)
						Array.Copy(result[resultIndex - 1], result[resultIndex], result[resultIndex].Length);
				}
			}
		}

		public static IEnumerable<T> Closure<T>(T root, Func<T, IEnumerable<T>> children)
		{
			return EnumerableHelpers.Return(root).Closure(children);
		}

		public static IEnumerable<T> Closure<T>(this IEnumerable<T> roots, Func<T, IEnumerable<T>> children)
		{
			return roots.Closure(x => x, (x, _) => children(x));
		}

		public static IEnumerable<TResult> Closure<T, TResult>(this IEnumerable<T> roots, Func<T, TResult> map,
			Func<T, TResult, IEnumerable<T>> children)
		{
			var seen = new HashSet<T>();
			var stack = new Stack<T>();
			foreach (var root in roots)
				stack.Push(root);
			while (stack.Count != 0)
			{
				var item = stack.Pop();
				var content = map(item);
				if (Equals(content, null))
					continue;
				if (seen.Contains(item))
					continue;
				seen.Add(item);
				yield return content;
				foreach (var child in children(item, content))
					stack.Push(child);
			}
		}

		public static int CombineHashCodes(int h1, int h2)
		{
			return ((h1 << 5) + h1) ^ h2;
		}
	}
}