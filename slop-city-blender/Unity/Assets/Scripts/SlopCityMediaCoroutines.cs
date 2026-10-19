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
        private bool disposing;

        internal void Start(MonoBehaviour owner, IEnumerator operation)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(operation);
            if (disposing || !owner || !owner.isActiveAndEnabled || !owner.gameObject.activeInHierarchy)
            { DisposeStack(stack); return; }
            active.Add(stack);
            try
            {
                // Unity can refuse startup without throwing when the owner has
                // become inactive. That iterator still needs its cleanup owner.
                if (owner.StartCoroutine(Drive(stack)) == null)
                { active.Remove(stack); DisposeStack(stack); }
            }
            catch { active.Remove(stack); DisposeStack(stack); throw; }
        }

        private IEnumerator Drive(Stack<IEnumerator> stack)
        {
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator current = stack.Peek();
                    bool advanced = current.MoveNext();
                    // MoveNext may synchronously disable its owner. DisposeAll
                    // then unwinds this exact stack; never pop or resume it again.
                    if (stack.Count == 0 || !ReferenceEquals(stack.Peek(), current)) yield break;
                    if (!advanced)
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
            if (disposing) return;
            disposing = true;
            try
            {
                // Teardown can run under memory pressure. Drain with the
                // HashSet's struct enumerator instead of allocating a copy after
                // Unity has stopped all owners and their finally blocks.
                while (active.Count > 0)
                {
                    Stack<IEnumerator> pending = null;
                    foreach (Stack<IEnumerator> stack in active) { pending = stack; break; }
                    active.Remove(pending);
                    DisposeStack(pending);
                }
            }
            finally { disposing = false; }
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
