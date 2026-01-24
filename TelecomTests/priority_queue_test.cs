using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace σκοπός {

[TestClass]
public class PriorityQueueTest {
  [TestMethod]
  public void HeapSort() {
    var heap = new PriorityQueue<char, int>();
    var tree = new SortedDictionary<int, char>();
    var random = new Random(1729);
    for (char c = '\x20'; c < '\x7F'; ++c) {
      int priority = random.Next();
      while (tree.ContainsKey(priority)) {
        priority = random.Next();
      }
      tree.Add(priority, c);
      heap.Enqueue(c, priority);
      Assert.IsTrue(heap.TryPeek(out char firstValue, out int firstKey));
      Assert.AreEqual(tree.First(), new KeyValuePair<int, char>(firstKey, firstValue));
    }
    var heap_sorted = new List<KeyValuePair<int, char>>(tree.Count);
    while (heap.TryDequeue(out char element, out int priority)) {
      heap_sorted.Add(new KeyValuePair<int, char>(priority, element));
    }
    Assert.IsFalse(heap.TryPeek(out _, out _));
    Assert.IsFalse(heap.TryDequeue(out _, out _));
    CollectionAssert.AreEqual(tree.ToArray(), heap_sorted);
  }

  [TestMethod]
  public void EqualPriorities() {
    var heap = new PriorityQueue<string, int>();
    heap.Enqueue("Probably important", 1);
    heap.Enqueue("Urgent", 0);
    heap.Enqueue("Also urgent", 0);
    heap.Enqueue("Everything is on fire", 0);
    heap.Enqueue("We might get to it eventually", 2);
    heap.Enqueue("Put this next to the other things that are on fire", 0);
    string element;
    int priority;
    Assert.IsTrue(heap.TryDequeue(out element, out priority));
    // Neither FIFO nor FILO on equal priorities.
    Assert.AreEqual("Urgent", element);
    Assert.AreEqual(0, priority);
    Assert.IsTrue(heap.TryDequeue(out element, out priority));
    Assert.AreEqual("Put this next to the other things that are on fire", element);
    Assert.AreEqual(0, priority);
    Assert.IsTrue(heap.TryDequeue(out element, out priority));
    Assert.AreEqual("Also urgent", element);
    Assert.AreEqual(0, priority);
    Assert.IsTrue(heap.TryDequeue(out element, out priority));
    Assert.AreEqual("Everything is on fire", element);
    Assert.AreEqual(0, priority);
    Assert.IsTrue(heap.TryDequeue(out element, out priority));
    Assert.AreEqual("Probably important", element);
    Assert.AreEqual(1, priority);
    Assert.IsTrue(heap.TryPeek(out element, out priority));
    Assert.AreEqual("We might get to it eventually", element);
    Assert.AreEqual(2, priority);
    Assert.IsTrue(heap.TryPeek(out _, out _));
    Assert.IsTrue(heap.TryDequeue(out element, out priority));
    Assert.AreEqual("We might get to it eventually", element);
    Assert.AreEqual(2, priority);
    Assert.IsFalse(heap.TryPeek(out _, out _));
    Assert.IsFalse(heap.TryDequeue(out _, out _));
  }
}

}
