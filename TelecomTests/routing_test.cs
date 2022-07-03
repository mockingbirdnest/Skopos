using Microsoft.VisualStudio.TestTools.UnitTesting;
using RealAntennas;
using RealAntennas.Antenna;
using System.Collections.Generic;

namespace σκοπός {
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
    var v = MakeNode(-1, 0);
    var w = MakeNode(+1, 0);
    var x = MakeNode(0, +1);
    var y = MakeNode(0, -1);
    Connect(v, x, 1, 0);
    Connect(v, y, 0, 1);
    Connect(w, x, 1, 0);
    Connect(w, y, 0, 1);
    Connect(x, y, 1, 0);
    Assert.IsNull(routing_.AvailabilityInIsolation(
        v, w,
        latency_limit: double.PositiveInfinity,
        one_way_data_rate: 1));
    Assert.AreEqual(routing_.AvailabilityInIsolation(
        source: v, 
        destinations: new[] {w},
        latency_limit: double.PositiveInfinity,
        data_rate: 1,
        out Routing.Channel[] v_w),
        Routing.PointToMultipointAvailability.Available);
    Assert.AreEqual(routing_.AvailabilityInIsolation(
        source: w,
        destinations: new[] {v},
        latency_limit: double.PositiveInfinity,
        data_rate: 1,
        out Routing.Channel[] w_v),
        Routing.PointToMultipointAvailability.Available);
  }

  RACommNode MakeNode(double x, double y) {
    RACommNode node = new RACommNode();
    node.precisePosition = new Vector3d(x, y, 0);
    node.RAAntennaList = new List<RealAntenna>();
    RealAntennaDigital antenna = new RealAntennaDigital();
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
