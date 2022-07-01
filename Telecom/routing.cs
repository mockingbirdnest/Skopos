using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;
using RealAntennas.Network;

namespace σκοπός {

public class Routing {

  public void Reset() {
    usage_.Clear();
  }

  public class Route {
    public readonly List<OrientedLink> links = new List<OrientedLink>();
    public double latency;
  }

  // Looks for the lowest-latency paths that supports the requested connection.
  // Reports these paths in |routes| and returns true if all necessary paths
  // were found.
  // Otherwise, returns false, setting |latency| to positive infinity.
  public bool FindRoute(Connection connection, out Route[] routes) {
    const double c = 299792458;
    // TODO(egg): consider using the stock intrusive data structure.
    var distances = new Dictionary<RACommNode, double>();
    var previous = new Dictionary<RACommNode, OrientedLink>();
    var boundary = new SortedDictionary<double, RACommNode>();
    var interior = new HashSet<RACommNode>();

    distances[connection.tx] = 0;
    boundary[0] = connection.tx;
    previous[connection.tx] = null;
    int rx_found = 0;
    routes = new Route[connection.rx.Count];

    while (boundary.Count > 0) {
      double tx_distance = boundary.First().Key;
      RACommNode tx = boundary.First().Value;
      boundary.Remove(tx_distance);

      if (tx_distance > connection.latency_threshold * c) {
        // We have run out of latency, no need to keep searching.
        return false;
      } else if (connection.rx.Contains(tx)) {
        int i = connection.rx.IndexOf(tx);
        routes[i] = new Route();
        for (OrientedLink link = previous[tx];
            link != null;
            link = previous[link.tx]) {
           routes[i].links.Add(link);
        }
        routes[i].links.Reverse();
        routes[i].latency = tx_distance / c;
        ++rx_found;
        if (rx_found == routes.Length) {
          return true;
        }
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

  public class OrientedLink {
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

  private readonly Dictionary<RACommLink, LinkUsage> usage_ = new Dictionary<RACommLink, LinkUsage>();
  private readonly Dictionary<RACommNode, Route> routes = new Dictionary<RACommNode, Route>();
}

}
