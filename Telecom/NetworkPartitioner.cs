using System.Collections.Generic;
using System.Linq;
using CommNet;

namespace σκοπός {
  public class NetworkPartitioner {
    public NetworkPartitioner() { }

    public readonly List<HashSet<CommNode>> partitions_ = new List<HashSet<CommNode>>();
    public readonly HashSet<CommNode> disconnected_partition_ = new HashSet<CommNode>();
    public readonly Dictionary<CommNode, HashSet<CommNode>> node_to_partition_map_ = new Dictionary<CommNode, HashSet<CommNode>>();

    private readonly HashSet<CommNode> nodesToCover_ = new HashSet<CommNode>();
    private readonly HashSet<CommNode> candidates_ = new HashSet<CommNode>();

    private void ClearPartitions() {
      foreach (var partition in partitions_) {
        partition.Clear();
      }
    }

    private void BuildPartition(CommNode start, HashSet<CommNode> partition, HashSet<CommNode> allNodes) {
      candidates_.Add(start);
      while (candidates_.Count > 0) {
        var candidate = candidates_.First();
        candidates_.Remove(candidate);
        partition.Add(candidate);
        allNodes.Remove(candidate);
        foreach (var node in candidate.Keys.Where(x => allNodes.Contains(x))) {
            candidates_.Add(node);
        }
      }
    }
    private void MapNodesToPartitions() {
      node_to_partition_map_.Clear();
      foreach (var partition in partitions_) {
        foreach (var node in partition) {
          node_to_partition_map_.Add(node, partition);
        }
      }
    }
    public void DiscoverPartitions(IEnumerable<CommNode> network) {
      ClearPartitions();
      if (partitions_.Count == 0) {
        partitions_.Add(disconnected_partition_);
      }

      nodesToCover_.Clear();
      foreach (var n in network) {
        nodesToCover_.Add(n);
      }

      disconnected_partition_.Clear();
      foreach (var n in nodesToCover_.Where(x => x.Keys.Count == 0)) { 
        disconnected_partition_.Add(n);
      }
      nodesToCover_.RemoveWhere(x => x.Keys.Count == 0);

      int numUsedPartitions = 1;
      while (nodesToCover_.Count > 0) {
        if (++numUsedPartitions > partitions_.Count) {
          partitions_.Add(new HashSet<CommNode>());
        }
        var partition = partitions_[numUsedPartitions - 1];
        BuildPartition(nodesToCover_.First(), partition, nodesToCover_);
      }

      MapNodesToPartitions();
    }
  }
}
