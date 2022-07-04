using Microsoft.VisualStudio.TestTools.UnitTesting;
using RealAntennas;
using RealAntennas.Antenna;
using System;
using System.Collections.Generic;
using System.Linq;

namespace σκοπός {

public static class TestingExtensions {
  public static RealAntennaDigital FirstDigitalAntenna(this RACommNode node) {
    return (RealAntennaDigital)node.RAAntennaList[0];
  }
}

[TestClass]
public class RoutingTest {
  [TestInitialize]
  public void Initialize() {
    BandInfo.All["C"] = new RealAntennas.Antenna.BandInfo{
         name = "C",
         TechLevel = 3,
         Frequency = 4.768e9f,
         ChannelWidth = 1.536e9f};
    BandInfo.initialized = true;
    TechLevelInfo.initialized = true;
    TechLevelInfo.All[0] = new TechLevelInfo{
        name = "TL0",
        Level = 0,
        Description = "WW2-era",
        PowerEfficiency = 0.0555f,
        ReflectorEfficiency = 0.5f,
        MinDataRate = 4,
        MaxDataRate = 4,
        MaxPower = 20,
        MassPerWatt = 1.6f,
        BaseMass = 1,
        BasePower = 2,
        BaseCost = 2,
        CostPerWatt = 5,
        ReceiverNoiseTemperature = 27000};
    TechLevelInfo.All[3] = new TechLevelInfo{
        name = "TL3",
        Level = 3,
        Description = "Interplanetary Comms, 1961-1963 [...]",
        PowerEfficiency = 0.1304f,
        ReflectorEfficiency = 0.56f,
        MinDataRate = 8,
        MaxDataRate = 64,
        MaxPower = 37,
        MassPerWatt = 1,
        BaseMass = 20.2f,
        BasePower = 19.5f,
        BaseCost = 50,
        CostPerWatt = 3,
        ReceiverNoiseTemperature = 5800};
    TechLevelInfo.MaxTL = 9;
    Encoder.All["Reed-Muller 1,3"] = new Encoder{
        name = "Reed-Muller 1,3",
        TechLevel = 3,
        CodingRate = 0.5f,
        RequiredEbN0 = 6.5f};
    Encoder.initialized = true;
  }

  [TestMethod]
  public void OverlappingDuplex() {
    // In this network, a circuit between v and w uses the same oriented edge
    // in both directions.
    //     x
    //   ↗   ↖
    // v   ↓  w
    //   ↖   ↗
    //     y
    var v = MakeNode("v", -1, 0);
    var w = MakeNode("w", +1, 0);
    var x = MakeNode("x", 0, +1);
    var y = MakeNode("y", 0, -1);
    // We have 1 Mbps against the arrows, which is too small to be relevant
    // for the connections requested.
    Connect(v, x, 20e6, 1e6);
    Connect(v, y, 1e6, 20e6);
    Connect(w, x, 20e6, 1e6);
    Connect(w, y, 1e6, 20e6);
    Connect(x, y, 20e6, 1e6);
    // We cannot get a circuit at 20 Mbps.
    Assert.IsNull(routing_.UseIfAvailable(
        v, w,
        latency_limit: double.PositiveInfinity,
        one_way_data_rate: 20e6));

    // But we could have simplex at 20 Mbps.
    Assert.AreEqual(
        Routing.PointToMultipointAvailability.Available,
        routing_.AvailabilityInIsolation(source: v,
                                         destinations: new[] {w},
                                         latency_limit: double.PositiveInfinity,
                                         data_rate: 20e6,
                                         out Routing.Channel[] v_w));
    Assert.AreEqual(
        Routing.PointToMultipointAvailability.Available,
        routing_.AvailabilityInIsolation(source: w,
                                         destinations: new[] {v},
                                         latency_limit: double.PositiveInfinity,
                                         data_rate: 20e6,
                                         out Routing.Channel[] w_v));
    CollectionAssert.AreEqual(
        (from link in v_w[0].links select link.rx).ToArray(),
        new[]{x, y, w});
    CollectionAssert.AreEqual(
        (from link in w_v[0].links select link.rx).ToArray(),
        new[]{x, y, v});

    // We can get a circuit at 10 Mbps.
    Routing.Circuit circuit = routing_.UseIfAvailable(
        v, w,
        latency_limit: double.PositiveInfinity,
        one_way_data_rate: 10e6);
    Assert.IsNotNull(circuit);
    CollectionAssert.AreEqual(
        (from link in circuit.forward.links select link.rx).ToArray(),
        new[]{x, y, w});
    CollectionAssert.AreEqual(
        (from link in circuit.backward.links select link.rx).ToArray(),
        new[]{x, y, v});

    // We are using half of the power of v and w, since at full power they could
    // transmit at 20 Mbps to x.
    Assert.AreEqual(0.5,
                    routing_.usage.TxPowerUsage(v.FirstDigitalAntenna()));
    Assert.AreEqual(0.5,
                    routing_.usage.TxPowerUsage(v.FirstDigitalAntenna()));
    // We are using all of the power of x, since that is what it takes to
    // transmit to y at 20 Mbps.
    Assert.AreEqual(1.0,
                    routing_.usage.TxPowerUsage(x.FirstDigitalAntenna()));
    // We using all of the power of y, we are using half of its full-power data
    // rate to both v and w.
    Assert.AreEqual(1.0,
                    routing_.usage.TxPowerUsage(x.FirstDigitalAntenna()));
    double bandwidth = BandInfo.All["C"].ChannelWidth;
    // Plenty of room left in C band though.
    // At this tech level we are using 1 Hz per bps, so 20 MHz at v and w
    // (10 MHz each from the uplink and downlink).
    Assert.AreEqual(
        20e6,
        routing_.usage.SpectrumUsage(v.FirstDigitalAntenna()) * bandwidth);
    Assert.AreEqual(
        20e6,
        routing_.usage.SpectrumUsage(w.FirstDigitalAntenna()) * bandwidth);
    // Plenty of room even at x and y, though it is a little more crowded.
    Assert.AreEqual(
        40e6,
        routing_.usage.SpectrumUsage(x.FirstDigitalAntenna()) * bandwidth);
    Assert.AreEqual(
        40e6,
        routing_.usage.SpectrumUsage(y.FirstDigitalAntenna()) * bandwidth);
  }

  RACommNode MakeNode(string name, double x, double y) {
    RACommNode node = new RACommNode();
    node.name = name;
    node.precisePosition = new Vector3d(x, y, 0);
    node.RAAntennaList = new List<RealAntenna>();
    RealAntennaDigital antenna = new RealAntennaDigital($"{name} C-band horn");
    var antenna_config = new ConfigNode();
    antenna_config.AddValue("TechLevel", "3");
    antenna_config.AddValue("RFBand", "C");
    antenna_config.AddValue("referenceGain", "58");
    antenna_config.AddValue("referenceFrequency", "4768");
    antenna_config.AddValue("TxPower", "63");
    antenna_config.AddValue("AMWTemp", "33");
    antenna.LoadFromConfigNode(antenna_config);
    node.RAAntennaList.Add(antenna);
    return node;
  }

  RACommLink Connect(
      RACommNode tx,
      RACommNode rx,
      double forward_data_rate,
      double backward_data_rate) {
    if (!tx.TryGetValue(rx, out CommNet.CommLink link)) {
      link = new RACommLink();
      link.Set(tx, rx, 0, 0);
      tx.Add(rx, link);
      rx.Add(tx, link);
    }
    var ra_link = (RACommLink)link;
    ra_link.FwdAntennaTx = tx.RAAntennaList[0];
    ra_link.FwdAntennaRx = rx.RAAntennaList[0];
    ra_link.RevAntennaTx = rx.RAAntennaList[0];
    ra_link.RevAntennaRx = tx.RAAntennaList[0];
    ra_link.FwdDataRate = forward_data_rate;
    ra_link.RevDataRate = backward_data_rate;
    return ra_link;
  }

  private Routing routing_ = new Routing();
}
}
