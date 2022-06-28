using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;

namespace σκοπός {

public class Routing {

  public void Reset() {
    usage_.Clear();
  }

  // Looks for the lowest-latency path that supports the requested connection.
  // If that path exists, returns true and uses up the data rate on all links,
  // setting |latency| to the latency of that path.
  // Otherwise, returns false, setting |latency| to positive infinity.
  public bool Connect(Connection connection, out double latency) {
    const double c = 299792458;
    // TODO(egg): consider using the stock intrusive data structure.
    var distances = new Dictionary<RACommNode, double>();
    var previous = new Dictionary<RACommNode, OrientedLink>();
    var boundary = new SortedDictionary<double, RACommNode>();
    var interior = new HashSet<RACommNode>();

    distances[connection.tx] = 0;
    boundary[0] = connection.tx;
    previous[connection.tx] = null;

    while (boundary.Count > 0) {
      double tx_distance = boundary.First().Key;
      RACommNode tx = boundary.First().Value;
      boundary.Remove(tx_distance);

      if (tx_distance > connection.latency_threshold * c) {
        // We have run out of latency, no need to keep searching.
        latency = double.PositiveInfinity;
        return false;
      } else if (tx == connection.rx) {
        // We have found a path.  Consume the data rate and return.
        for (OrientedLink link = previous[tx];
             link != null;
             link = previous[link.tx]) {
          if (!usage_.TryGetValue(link.ra_link, out LinkUsage usage)) {
            usage_.Add(link.ra_link, usage = new LinkUsage());
          }
          DirectedLinkUsage directed_usage = link.forward ? usage.forward
                                                          : usage.backward;
          directed_usage.data_rate -= connection.data_rate;
          directed_usage.connections.Add(connection);
        }
        latency = tx_distance / c;
        return true;
      }

      interior.Add(tx);

      // TODO(egg): continue if tx is an rx-only ground station.

      foreach (var node_link in tx) {
        var rx = node_link.Key as RACommNode;
        var link = node_link.Value as RACommLink;
        if (interior.Contains(rx)) {
          continue;
        }

        bool forward = link.b == rx;
        double data_rate = forward ? link.FwdDataRate : link.RevDataRate;
        if (usage_.TryGetValue(link, out LinkUsage usage)) {
          data_rate -= forward ? usage.forward.data_rate
                               : usage.backward.data_rate;
        }
        if (data_rate < connection.data_rate) {
          continue;
        }

        // TODO(egg): this needs to be adapted once we have support for
        // landlines.
        double link_length = (link.a.position - link.b.position).magnitude;

        double tentative_distance = tx_distance + link_length;
        if (distances.TryGetValue(rx, out double d)) {
          if (d <= tentative_distance) {
            continue;
          } else {
            boundary.Remove(d);
          }
        }

        distances[rx] = tentative_distance;
        // NOTE(egg): this will fail if we have equidistant nodes.
        boundary.Add(tentative_distance, rx);
        previous[rx] = new OrientedLink(tx,rx,link, forward);
      }
    }
    latency = double.PositiveInfinity;
    return false;
  }

  private class LinkUsage {
    public readonly DirectedLinkUsage forward = new DirectedLinkUsage();
    public readonly DirectedLinkUsage backward = new DirectedLinkUsage();
  }

  private class DirectedLinkUsage {
    public double data_rate = 0;
    public readonly List<Connection> connections = new List<Connection>();
  }

  private class OrientedLink {
    public OrientedLink(RACommNode tx,
                        RACommNode rx,
                        RACommLink ra_link,
                        bool forward) {
      this.tx = tx;
      this.rx = rx;
      this.ra_link = ra_link;
      this.forward = forward;
    }

    public RACommNode tx;
    public RACommNode rx;
    public RACommLink ra_link;
    public bool forward;
  }

  private Dictionary<RACommLink, LinkUsage> usage_;
}

}
