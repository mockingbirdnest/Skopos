using System;
using System.Collections.Generic;
using System.Linq;
using CommNet.Network;
using RealAntennas;
using static σκοπός.Routing.PointToMultipointAvailability;

namespace σκοπός {

  public class Routing {

  public enum PointToMultipointAvailability {
    Unavailable,
    Partial,
    Available,
  }

  public class Channel {
    public readonly List<OrientedLink> links = new List<OrientedLink>();
    public double latency;
  }

  public struct SourcedLink {
    public SourcedLink(Connection connection,
                       Channel channel,
                       OrientedLink link) {
      this.connection = connection;
      this.channel = channel;
      this.link = link;
    }
    public readonly Connection connection;
    public readonly Channel channel;
    public readonly OrientedLink link;
  }

  public class Circuit {
    public readonly Channel forward;
    public readonly Channel backward;

    public Circuit(Channel forward, Channel backward) {
      this.forward = forward;
      this.backward = backward;
    }

      public double round_trip_latency => forward.latency + backward.latency;
    }

  public class NetworkUsage {
    public class PowerBreakdown {
      public struct SingleUsage {
        public SourcedLink link;
        public double power ;
      }

      public double power { get; private set; } = 0;

      public void AddUsages(SingleUsage[] broadcast) {
        power += (from usage in broadcast select usage.power).Max();
        usages_.Add(broadcast);
      }

      public PowerBreakdown Clone() {
        return new PowerBreakdown{
          usages_ = usages.Select(usages => usages.ToArray()).ToList(),
          power = power,
        };
      }

      public IEnumerable<SingleUsage[]> usages => usages_;

      private List<SingleUsage[]> usages_ = new List<SingleUsage[]>();
    }

    public class SpectrumBreakdown {
      public struct SingleUsage {
        public enum Kind { Transmit, Receive }
        public SourcedLink link;
        public Kind kind;
        public double spectrum ;
      }

      public double spectrum { get; private set; } = 0;

      public void AddUsages(SingleUsage[] usage) {
        spectrum += usage[0].spectrum;
        usages_.Add(usage);
      }

      public SpectrumBreakdown Clone() {
        return new SpectrumBreakdown{
          usages_ = usages.Select(usages => usages.ToArray()).ToList(),
          spectrum = spectrum
        };
      }

      public IEnumerable<SingleUsage[]> usages => usages_;

      private List<SingleUsage[]> usages_ = new List<SingleUsage[]>();
    }

    public static NetworkUsage None = new NetworkUsage();

    // Normalized on [0, 1];
    public double TxPowerUsage(RealAntennaDigital tx) {
      return SourcedTxPowerUsage(tx).power;
    }

    // In Hz.
    public double SpectrumUsage(RealAntennaDigital trx) {
      return SourcedSpectrumUsage(trx).spectrum;
    }

    public virtual PowerBreakdown SourcedTxPowerUsage(
        RealAntennaDigital tx) {
      return NoPowerUsage;
     }

    public virtual SpectrumBreakdown SourcedSpectrumUsage(
        RealAntennaDigital tx) {
      return NoSpectrumUsage;
    }
    public virtual IEnumerable<RealAntennaDigital> Transmitters() { yield break; }
    public virtual IEnumerable<RealAntennaDigital> Users() { yield break; }
    protected NetworkUsage() {}

    protected static PowerBreakdown NoPowerUsage = new PowerBreakdown();
    protected static SpectrumBreakdown NoSpectrumUsage = new SpectrumBreakdown();
  }

  public Routing() {
    current_network_usage_ = new RoutingNetworkUsage(this);
  }

  public void Reset(IEnumerable<RACommNode> tx_only,
                    IEnumerable<RACommNode> rx_only,
                    IEnumerable<RACommNode> multiple_tracking_tx) {
    OrientedLink.ReturnLinks(this);
    links_.Clear();
    current_network_usage_.Clear();

    tx_only_ = new HashSet<RACommNode>(tx_only);
    rx_only_ = new HashSet<RACommNode>(rx_only);
    multiple_tracking_ = new HashSet<RACommNode>(multiple_tracking_tx);
  }
  public NetworkUsage usage => current_network_usage_;

  public bool IsLimited(RACommNode node) {
    return !multiple_tracking_.Contains(node);
  }

  public Circuit FindCircuitInIsolation(
      RACommNode source,
      RACommNode destination,
      double round_trip_latency_limit,
      double one_way_data_rate) {
    return FindCircuit(source,
                       destination,
                       round_trip_latency_limit,
                       one_way_data_rate,
                       NetworkUsage.None);
  }

  public Circuit FindAndUseAvailableCircuit(
      RACommNode source,
      RACommNode destination,
      double round_trip_latency_limit,
      double one_way_data_rate,
      Connection connection) {
    Circuit circuit = FindCircuit(
        source,
        destination,
        round_trip_latency_limit,
        one_way_data_rate,
        current_network_usage_);
    if (circuit != null) {
      foreach (OrientedLink link in circuit.forward.links) {
        current_network_usage_.UseLinks(
            new[] {new SourcedLink(connection, circuit.forward, link)},
            one_way_data_rate);
      }
      foreach (OrientedLink link in circuit.backward.links) {
        current_network_usage_.UseLinks(
            new[] {new SourcedLink(connection, circuit.backward, link)},
            one_way_data_rate);
      }
    }
    return circuit;
  }

  public PointToMultipointAvailability FindChannelsInIsolation(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      out Channel[] channels) {
    return FindChannels(source,
                        destinations,
                        latency_limit,
                        data_rate,
                        NetworkUsage.None,
                        out channels);
  }

  public PointToMultipointAvailability FindAndUseAvailableChannels(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      out Channel[] channels,
      Connection connection) {
    PointToMultipointAvailability availability = FindChannels(
        source,
        destinations,
        latency_limit,
        data_rate,
        current_network_usage_,
        out channels);
    if (availability != Unavailable) {
      var links_by_tx_antenna =
          from channel in channels where channel != null
          from link in channel.links
          group new SourcedLink(connection, channel, link) by link.tx_antenna;
      foreach (var links in links_by_tx_antenna) {
        current_network_usage_.UseLinks(links, data_rate);
      }
    }
    return availability;
  }

  private Circuit FindCircuit(RACommNode source,
                              RACommNode destination,
                              double round_trip_latency_limit,
                              double one_way_data_rate,
                              NetworkUsage usage) {
    if (FindChannels(source,
                     new[]{destination},
                     round_trip_latency_limit,
                     one_way_data_rate,
                     usage,
                     out Channel[] forward) == Unavailable) {
      return null;
    }
    var usage_with_forward_channel = new RoutingNetworkUsage(this, usage);
    foreach (var link in forward[0].links) {
      usage_with_forward_channel.UseLinks(new[]{link.Unsourced()},
                                          one_way_data_rate);
    }
    if (FindChannels(destination,
                     new[]{source},
                     round_trip_latency_limit - forward[0].latency,
                     one_way_data_rate,
                     usage_with_forward_channel,
                     out Channel[] backward) == Unavailable) {
      return null;
    }
    return new Circuit(forward[0], backward[0]);
  }
    
  private PointToMultipointAvailability FindChannels(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel[] channels) {

    if (Telecom.Instance.one_hop_optimize && destinations.Count == 1) {
      Channel ans;
      if (FindChannelsOneHop(source, destinations[0], latency_limit, data_rate, usage, out ans) == PointToMultipointAvailability.Available) {
        channels = new Channel[1] {ans};
        return PointToMultipointAvailability.Available;
      }
    }
    const double c = 299792458;
    double latency_distance = c * latency_limit;
    // TODO(egg): consider using the stock intrusive data structure.
    var distances = new Dictionary<RACommNode, double>();
    var previous = new Dictionary<RACommNode, OrientedLink>();
    var boundary = new PriorityQueue<RACommNode, double>();
    var interior = new HashSet<RACommNode>();
    
    // Dijkstra’s algorithm without DecreaseKey.
    distances[source] = 0;
    boundary.Enqueue(source, 0);
    previous[source] = null;
    int rx_found = 0;
    channels = new Channel[destinations.Count];
    bool is_point_to_multipoint = destinations.Count > 1;
    while (boundary.TryDequeue(out RACommNode tx, out double tx_distance)) {
      if (tx_distance != distances[tx]) {
        // We have already considered `tx` through a shorter path.
        continue;
      }
      if (tx_distance > latency_limit * c) {
        // We have run out of latency, no need to keep searching.
        return rx_found == 0 ? Unavailable : Partial;
      } else if (destinations.Contains(tx)) {
        int i = destinations.IndexOf(tx);
        channels[i] = new Channel();
        for (OrientedLink link = previous[tx];
             link != null;
             link = previous[link.tx]) {
           channels[i].links.Add(link);
        }
        channels[i].links.Reverse();
        channels[i].latency = tx_distance / c;
        ++rx_found;
        if (rx_found == channels.Length) {
          return PointToMultipointAvailability.Available;
        }
      }

      interior.Add(tx);

      if (rx_only_.Contains(tx)) {
        continue;
      }

      foreach (var stock_rx in tx.Keys) {
        var rx = (RACommNode)stock_rx;

        if (tx_only_.Contains(rx) || interior.Contains(rx)) {
          continue;
        }

        var link = OrientedLink.Get(this, from: tx, to: rx);
        if (link.max_data_rate < data_rate) { // Best-case data rate check.
          continue;
        }

        double tentative_distance = distances[tx] + link.length; 
        if (tentative_distance > latency_distance || 
            (distances.TryGetValue(rx, out double d) &&
            tentative_distance > d)) { // Latency optimality check
          continue;
        }

        // Tx power check.
        if (link.max_data_rate * (1 - usage.TxPowerUsage(link.tx_antenna)) < data_rate) {
          continue;
        }

        double max_used_spectrum = link.band.ChannelWidth * link.bits_per_symbol - data_rate;
        // Rx bandwidth check.
        if (usage.SpectrumUsage(link.rx_antenna) * link.bits_per_symbol > max_used_spectrum) {
          continue;
        }

        // Tx bandwidth check.
        if (usage.SpectrumUsage(link.tx_antenna) * link.bits_per_symbol > max_used_spectrum) {
          continue;
        }
        distances[rx] = tentative_distance;
        previous[rx] = link;
        boundary.Enqueue(rx, tentative_distance);
      }
    }
    return rx_found == 0 ? Unavailable : Partial;
  }

  private PointToMultipointAvailability FindChannelsOneHop(
      RACommNode source,
      RACommNode destination,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel channel) {
    const double c = 299792458;
    double latency_distance = c * latency_limit;
    channel = new Channel();
    double best_distance = latency_distance;
    foreach (RACommNode relay in relay_vessels_) {
      if (source.TryGetValue(relay, out var inbound) && relay.TryGetValue(destination, out var outbound)) {
        // Checks in this order, roughly in the order I expect them to fail.
        // - Best outbound data rate is too poor.
        // - Best inbound data rate is too poor.
        // - This latency is already worst than the best one.
        // - Outbound antenna doesn't have enough power
        // - Outbound antenna doesn't have enough bandwidth
        // - Inbound antenna doesn't have enough bandwidth
        // - Source antenna doesn't have enough power
        // - Source antenna doesn't have enough bandwidth
        // - Destination antenna doesn't have enough bandwidth

        OrientedLink outbound_link = OrientedLink.Get(this, from: relay, to: destination);
        if (outbound_link.max_data_rate < data_rate) {
          continue;
        }

        OrientedLink inbound_link = OrientedLink.Get(this, from: source, to: relay);
        if (inbound_link.max_data_rate < data_rate) {
          continue;
        }

        double distance = outbound_link.length + inbound_link.length;
        if (distance > best_distance) {
          continue;
        }

        if (outbound_link.max_data_rate * (1 - usage.TxPowerUsage(outbound_link.tx_antenna)) < data_rate) {
          continue;
        }
        
        double outbound_max_used_data_rate = outbound_link.band.ChannelWidth * outbound_link.bits_per_symbol - data_rate;
        if (usage.SpectrumUsage(outbound_link.tx_antenna) * outbound_link.bits_per_symbol > outbound_max_used_data_rate) {
          continue;
        }
        
        double inbound_max_used_data_rate = inbound_link.band.ChannelWidth * inbound_link.bits_per_symbol - data_rate;
        if (usage.SpectrumUsage(inbound_link.rx_antenna) * inbound_link.bits_per_symbol > inbound_max_used_data_rate) {
          continue;
        }

        #if NEVER
        // This checks that we don't overrun spectrum if we doubledip the same antenna. This is allowed by the default routing algorithm, so I'm leaving it as-is.
        if (inbound_link.rx_antenna == outbound_link.tx_antenna) {
          double total_required_spectrum = data_rate / outbound_link.bits_per_symbol + data_rate / inbound_link.bits_per_symbol;
          if (usage.SpectrumUsage(inbound_link.tx_antenna) + total_required_spectrum > outbound_link.band.ChannelWidth) {
            continue;
          }
        }
        #endif
        
        if (inbound_link.max_data_rate * (1 - usage.TxPowerUsage(inbound_link.tx_antenna)) < data_rate) {
          continue;
        }

        if (usage.SpectrumUsage(inbound_link.tx_antenna) * inbound_link.bits_per_symbol > inbound_max_used_data_rate) {
          continue;
        }

        if (usage.SpectrumUsage(outbound_link.rx_antenna) * outbound_link.bits_per_symbol > outbound_max_used_data_rate) {
          continue;
        }

        // All checks passed. (Goodness.)
        best_distance = distance;
        channel.links.Clear();
        channel.links.Add(inbound_link);
        channel.links.Add(outbound_link);
      }
    }
    if (best_distance < latency_distance) return PointToMultipointAvailability.Available;
    else return PointToMultipointAvailability.Unavailable;
  }

  public void FindRelays() {
    relay_vessels_.Clear();
    var home_body = FlightGlobals.GetHomeBody();
    foreach (RACommNode node in (CommNet.CommNetNetwork.Instance?.CommNet as RACommNetwork).Nodes) {
      if (node.ParentVessel?.mainBody == home_body && node.RAAntennaList.Any(ra => ra.RFBand.ChannelWidth >= 100e6)) { 
        // Vessels that orbit around Earth and have a wideband antenna
        relay_vessels_.Add(node);  
      }
    }
  }

  private class LinkUsage {
    public readonly DirectedLinkUsage forward = new DirectedLinkUsage();
    public readonly DirectedLinkUsage backward = new DirectedLinkUsage();
  }

  private class DirectedLinkUsage {
    public double data_rate = 0;
    public readonly List<PointToMultipointConnection> connections = new List<PointToMultipointConnection>();
  }

  private class RoutingNetworkUsage : NetworkUsage {
    public RoutingNetworkUsage(Routing routing) {
      routing_ = routing;
    }

    public RoutingNetworkUsage(Routing routing, NetworkUsage other)
        : this(routing) {
      if (other is RoutingNetworkUsage nontrival) {
        tx_power_usage_ = nontrival.tx_power_usage_.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Clone());
        spectrum_usage_ = nontrival.spectrum_usage_.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Clone());
      }
    }

    public void Clear() {
      tx_power_usage_.Clear();
      spectrum_usage_.Clear();
    }

    public override PowerBreakdown SourcedTxPowerUsage(RealAntennaDigital tx) {
      if (tx_power_usage_.TryGetValue(tx, out PowerBreakdown usage)) {
        return usage;
      }
      return NoPowerUsage;
    }

    public override SpectrumBreakdown SourcedSpectrumUsage(
        RealAntennaDigital rx) {
      if (spectrum_usage_.TryGetValue(rx, out SpectrumBreakdown usage)) {
        return usage;
      }
      return NoSpectrumUsage;
    }

    public override IEnumerable<RealAntennaDigital> Transmitters() {
      foreach (var antenna in tx_power_usage_.Keys) {
        if (antenna is RealAntennaDigital digital) {
          yield return digital;
        }
      }
    }

    public override IEnumerable<RealAntennaDigital> Users() {
      foreach (var antenna in spectrum_usage_.Keys) {
        if (antenna is RealAntennaDigital digital) {
          yield return digital;
        }
      }
    }

    // The links must all share the same tx antenna and tech level.
    // Uses tx power corresponding to broadcast at the given data rate along
    // all of these links (thus at the power needed for the weakest link).
    // Also uses the necessary spectrum on all antennas involved.
    public void UseLinks(IEnumerable<SourcedLink> links,
                         double data_rate) {
      EnsureSameTxAntennaAndTL(from sourced in links select sourced.link);
      UseTxPower(links, data_rate);
      UseSpectrum(links, data_rate);
    }

    private void UseTxPower(IEnumerable<SourcedLink> links,
                            double data_rate) {
      if (routing_.multiple_tracking_.Contains(links.First().link.tx)) {
        return;
      }
      RealAntennaDigital tx_antenna = links.First().link.tx_antenna;
      if (!tx_power_usage_.ContainsKey(tx_antenna)) {
        tx_power_usage_.Add(tx_antenna, new PowerBreakdown());
      }
      var usages = (from sourced in links
                    select new PowerBreakdown.SingleUsage{
                        link = sourced,
                        power = sourced.link.TxPowerUsageFromDataRate(data_rate),
                    }).ToArray();
      tx_power_usage_[tx_antenna].AddUsages(usages);
    }

    private void UseSpectrum(IEnumerable<SourcedLink> links, double data_rate) {
      double usage = links.First().link.SpectrumUsageFromDataRate(data_rate);
      foreach (var sourced in links.GroupBy(l => l.link.rx_antenna)) {
        RACommNode rx = sourced.First().link.rx;
        RealAntennaDigital rx_antenna = sourced.First().link.rx_antenna;
        if (routing_.multiple_tracking_.Contains(rx)) {
          continue;
        }
        if (!spectrum_usage_.ContainsKey(rx_antenna)) {
          spectrum_usage_.Add(rx_antenna, new SpectrumBreakdown());
        }
        spectrum_usage_[rx_antenna].AddUsages(
            (from link in sourced select
                new SpectrumBreakdown.SingleUsage{
                    link = link,
                    kind = SpectrumBreakdown.SingleUsage.Kind.Receive,
                    spectrum = usage,
            }).ToArray());
      }
      RealAntennaDigital tx_antenna = links.First().link.tx_antenna;
      if (routing_.multiple_tracking_.Contains(links.First().link.tx)) {
        return;
      }
      if (!spectrum_usage_.ContainsKey(tx_antenna)) {
        spectrum_usage_.Add(tx_antenna, new SpectrumBreakdown());
      }
      spectrum_usage_[tx_antenna].AddUsages(
          (from link in links select
            new SpectrumBreakdown.SingleUsage{
                link = link,
                kind = SpectrumBreakdown.SingleUsage.Kind.Transmit,
                spectrum = usage,
          }).ToArray());
    }

    private void EnsureSameTxAntennaAndTL(IEnumerable<OrientedLink> links) {
#if NEVER
      RealAntennaDigital tx_antenna = links.First().tx_antenna;
      var antennas = from link in links select link.tx_antenna;
      if (antennas.Any(tx => tx != tx_antenna)) {
        throw new ArgumentException("Broadcast from multiple antennas");
      }
      int tech_level = links.First().tech_level;
      var tech_levels = from link in links select link.tech_level;
      if (tech_levels.Any(tl => tl != tech_level)) {
        throw new ArgumentException("Broadcast at multiple tech levels");
      }
#endif
    }

    private readonly Dictionary<RealAntenna, PowerBreakdown> tx_power_usage_ =
        new Dictionary<RealAntenna, PowerBreakdown>();
    private readonly Dictionary<RealAntenna, SpectrumBreakdown> spectrum_usage_ =
        new Dictionary<RealAntenna, SpectrumBreakdown>();
    private Routing routing_;
  }

  public class OrientedLink {
    private static readonly Queue<OrientedLink> pool = new Queue<OrientedLink>();
    private static OrientedLink GetFromPool() => pool.Count > 0 ? pool.Dequeue() : new OrientedLink();
    internal static void ReturnLinks(Routing r) {
      foreach (var link in r.links_.Values) {
        link.Clear();
        pool.Enqueue(link);
      }
    }
    public static OrientedLink Get(
        Routing routing,
        RACommNode from,
        RACommNode to) {
      if (!routing.links_.TryGetValue((from, to), out OrientedLink link)) {
        var ra_link = (RACommLink)from[to];
        bool forward = ra_link.a == from;
        link = GetFromPool();
        link.Set(from, to, ra_link, forward, routing);
        routing.links_.Add((from, to), link);
      }
      return link;
    }

    public SourcedLink Unsourced() {
      return new SourcedLink(null, null, this);
    }

    public RACommNode tx { get; private set; }
    public RACommNode rx { get; private set; }
    public RACommLink ra_link { get; private set; }
    public bool forward { get; private set; }

    public RealAntennaDigital tx_antenna =>
        (RealAntennaDigital)(forward ? ra_link.FwdAntennaTx
                                     : ra_link.RevAntennaTx);
    public RealAntennaDigital rx_antenna =>
        (RealAntennaDigital)(forward ? ra_link.FwdAntennaRx
                                     : ra_link.RevAntennaRx);
    public double max_data_rate => forward ? ra_link.FwdDataRate
                                           : ra_link.RevDataRate;
    public int tech_level => Math.Min(tx_antenna.TechLevelInfo.Level,
                                      rx_antenna.TechLevelInfo.Level);
    public RealAntennas.Antenna.BandInfo band => tx_antenna.RFBand;
    // TODO(egg): we only care about encoding and modulation; but while TL 3 and
    // 4 have the same encoder, they differ in modulation (QPSK vs. 8PSK), so it
    // doesn’t matter that much.
    public bool is_at_tx_tech_level =>
        tech_level == tx_antenna.TechLevelInfo.Level;
    public RealAntennaDigital lowest_tech_antenna =>
        is_at_tx_tech_level ? tx_antenna : rx_antenna;
    public RAModulator modulator => lowest_tech_antenna.modulator;
    public RealAntennas.Antenna.Encoder encoder => lowest_tech_antenna.Encoder;
    // TODO(egg): this needs to be adapted once we have support for landlines.
    public double length => (tx.precisePosition - rx.precisePosition).magnitude;

    public double CapacityWithUsage(NetworkUsage usage) {
      double available_spectrum =
          band.ChannelWidth - Math.Max(usage.SpectrumUsage(tx_antenna),
                                       usage.SpectrumUsage(rx_antenna));
      double bandwidth_limited_data_rate =
          available_spectrum * bits_per_symbol_; // Max data rate check is already built into the power check
      double power_limited_data_rate =
          max_data_rate * (1 - usage.TxPowerUsage(tx_antenna));
      return Math.Min(bandwidth_limited_data_rate, power_limited_data_rate);
    }

    public double TxPowerUsageFromDataRate(double data_rate) {
      return data_rate / max_data_rate;
    }

    public double SpectrumUsageFromDataRate(double data_rate) {
      return data_rate / bits_per_symbol_;
    }

    private OrientedLink() { }
    private OrientedLink(RACommNode tx,
                         RACommNode rx,
                         RACommLink ra_link,
                         bool forward,
                         Routing routing) {
      Set(tx, rx, ra_link, forward, routing);
    }

    private void Clear() => Set(null, null, null, true, null);
    private void Set(RACommNode tx, RACommNode rx, RACommLink ra_link, bool forward, Routing routing) {
      this.tx = tx;
      this.rx = rx;
      this.ra_link = ra_link;
      this.forward = forward;
      routing_ = routing;
      if (ra_link != null) {
        bits_per_symbol_ = encoder.CodingRate * modulator.ModulationBits;
      }
    }

    public double bits_per_symbol => bits_per_symbol_;
    private double bits_per_symbol_;

    private Routing routing_;
  }

  private readonly RoutingNetworkUsage current_network_usage_;

  private readonly Dictionary<(RACommNode, RACommNode), OrientedLink> links_ =
      new Dictionary<(RACommNode, RACommNode), OrientedLink>();

  private readonly List<RACommNode> relay_vessels_ = new List<RACommNode>();
  
  // Stations only capable of transmitting.
  private HashSet<RACommNode> tx_only_ = new HashSet<RACommNode>();
  // Station only capable of receiving.
  private HashSet<RACommNode> rx_only_ = new HashSet<RACommNode>();
  // Station modelled as capable of tracking multiple targets simultaneously,
  // so that each of its antennas really represents multiple independent
  // antennas.  Neither their transmitted power nor their spectrum get used up.
  private HashSet<RACommNode> multiple_tracking_ = new HashSet<RACommNode>();
}

}
