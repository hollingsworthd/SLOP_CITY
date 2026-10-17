using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Unity stops coroutines without disposing their iterators. Media owners must
    /// explicitly unwind pending using/finally blocks after stopping their coroutines.
    /// </summary>
    internal sealed class SlopCityMediaCoroutines
    {
        private readonly HashSet<Stack<IEnumerator>> active = new HashSet<Stack<IEnumerator>>();

        internal void Start(MonoBehaviour owner, IEnumerator operation)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(operation);
            active.Add(stack);
            try { owner.StartCoroutine(Drive(stack)); }
            catch { active.Remove(stack); DisposeStack(stack); throw; }
        }

        private IEnumerator Drive(Stack<IEnumerator> stack)
        {
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator current = stack.Peek();
                    if (!current.MoveNext())
                    {
                        stack.Pop();
                        (current as IDisposable)?.Dispose();
                        continue;
                    }
                    object yielded = current.Current;
                    // Preserve Unity's own wait handling (including custom realtime
                    // waits). Flatten only nested iterators whose cleanup we own.
                    if (yielded is IEnumerator nested && !(yielded is CustomYieldInstruction)) stack.Push(nested);
                    else yield return yielded;
                }
            }
            finally { active.Remove(stack); DisposeStack(stack); }
        }

        // Call after the owner's StopAllCoroutines, so no iterator can resume while
        // its request, native stream or uncommitted Unity object is being released.
        internal void DisposeAll()
        {
            var pending = new List<Stack<IEnumerator>>(active);
            active.Clear();
            foreach (Stack<IEnumerator> stack in pending) DisposeStack(stack);
        }

        private static void DisposeStack(Stack<IEnumerator> stack)
        {
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Pop();
                try { (current as IDisposable)?.Dispose(); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }
    }
}
