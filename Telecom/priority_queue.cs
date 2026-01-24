using System;
using System.Collections.Generic;

namespace σκοπός {

// An implementation of System.Collections.Generic.PriorityQueue from .NET 6,
// since we are stuck with the .NET Framework 4.8.
// Only the subset of the API that is actually used by Σκοπός is implemented.
// We implement it as a quaternary heap like .NET, see
// https://github.com/dotnet/dotnet/blob/v10.0.0/src/runtime/src/libraries/System.Collections/src/System/Collections/Generic/PriorityQueue.cs
public class PriorityQueue<TElement, TPriority> {
  public void Enqueue(TElement element, TPriority priority) {
    // TODO(egg): We could save this initialization by managing the array
    // ourselves as in the .NET implementation instead of letting List do that.
    nodes_.Add((default, default));
    InsertAbove((element, priority), nodes_.Count - 1) ;
  }

  public bool TryPeek(out TElement element, out TPriority priority) {
    if (nodes_.Count == 0) {
      element = default;
      priority = default;
      return false;
    }
    (element, priority) = nodes_[0];
    return true;
  }

  public bool TryDequeue(out TElement element, out TPriority priority) {
    if (TryPeek(out element, out priority)) {
      if (nodes_.Count == 1) {
        nodes_.Clear();
      } else {
        var last_node = nodes_[nodes_.Count - 1];
        nodes_.RemoveAt(nodes_.Count - 1) ;
        InsertBelow(last_node, 0) ;
      }
      return true;
    }
    return false;
  }

  // Inserts `node` on the path between the root and the node at `i`, at the
  // position determined by its priority, moving its children down.  This
  // operation overwrites the node at `i`.
  private void InsertAbove((TElement element, TPriority priority) node, int i) {
    while (i > 0) {
      int p = parent_index(i);
      var parent = nodes_[p];
      if (Comparer<TPriority>.Default.Compare(node.priority, parent.priority) < 0) {
        nodes_[i] = parent;
        i = p;
      } else {
        break;
      }
    }
    nodes_[i] = node;
  }

  // Inserts `node` in the subtree rooted at `i`, moving its ancestors up.  This
  // operation overwrites the node at `i`.
  private void InsertBelow((TElement element, TPriority priority) node, int subtree_root) {
    for (int c = leftmost_child_index(subtree_root); c < nodes_.Count;
         c = leftmost_child_index(subtree_root)) {
      var first_child_in_queue = nodes_[c];
      int first_child_in_queue_index = c;
      int past_last_child = Math.Min(c + max_children_, nodes_.Count);
      for (; c < past_last_child; ++c) {
        var child = nodes_[c];
        if (Comparer<TPriority>.Default.Compare(child.priority,
                                                first_child_in_queue.priority) < 0) {
          first_child_in_queue = child;
          first_child_in_queue_index = c;
        }
      }
      if (Comparer<TPriority>.Default.Compare(node.priority,
                                              first_child_in_queue.priority) <= 0) {
        break;
      }
      nodes_[subtree_root] = first_child_in_queue;
      subtree_root = first_child_in_queue_index;
    }
    nodes_[subtree_root] = node;
  }

  private static int parent_index(int i) {
    return (i - 1) >> log2_max_children_;
  }

  private static int leftmost_child_index(int i) {
    return (i << log2_max_children_) + 1;
  }

  private const int max_children_ = 4;
  private const int log2_max_children_ = 2;
  private List<(TElement element, TPriority priority)> nodes_ =
      new List<(TElement element, TPriority priority)>();
}

}