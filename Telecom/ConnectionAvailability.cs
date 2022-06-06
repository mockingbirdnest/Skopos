using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractConfigurator;
using Contracts;
using RealAntennas;

namespace σκοπός {
  public class ConnectionAvailabilityFactory : ParameterFactory {
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      ok &= ConfigNodeUtil.ParseValue<string>(node, "connection", x => connection_ = x, this);
      ok &= ConfigNodeUtil.ParseValue<double>(node, "availability", x => availability_ = x, this);
      return ok;
    }
    public override ContractParameter Generate(Contract contract) {
      return new ConnectionAvailability(connection_, availability_);
    }
    private string connection_;
    private double availability_;
  }

  public class ConnectionAvailability : ContractParameter {
    private string last_title_;
    private TitleTracker title_tracker_;
    public ConnectionAvailability() {
      title_tracker_ = new TitleTracker(this);
    }

    public ConnectionAvailability(string connection, double availability) {
      title_tracker_ = new TitleTracker(this);
      connection_ = connection;
      availability_ = availability;
      disableOnStateChange = false;
    }

    protected override void OnUpdate() {
      base.OnUpdate();
      var connection = Telecom.Instance.network.Monitor(connection_);
      if (connection.days >= connection.window &&
          connection.availability >= availability_) {
        SetComplete();
      } else {
        SetIncomplete();
      }
      GetTitle();
    }

    protected override void OnLoad(ConfigNode node) {
      connection_ = node.GetValue("connection");
      availability_ = double.Parse(node.GetValue("availability"));
    }

    protected override void OnSave(ConfigNode node) {
      node.AddValue("connection", connection_);
      node.AddValue("availability", availability_);
    }

    protected override string GetMessageComplete() {
      return "meow complete";
    }

    protected override string GetMessageIncomplete() {
      return "meow incomplete";
    }

    protected override string GetMessageFailed() {
      return "meow failed";
    }

    protected override string GetTitle() {
      var connection = Telecom.Instance.network.GetConnection(connection_);
      var tx = Telecom.Instance.network.GetStation(connection.tx_name);
      var rx = Telecom.Instance.network.GetStation(connection.rx_name);
      string data_rate = RATools.PrettyPrintDataRate(connection.rate_threshold);
      double latency = connection.latency_threshold;
      string pretty_latency = latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";
      string status = connection.within_sla ? "connected" : "disconnected";
      string title = $"Connect {tx.displaynodeName} to {rx.displaynodeName}:\n" +
             $"At least {data_rate}, with a latency of at most {pretty_latency} (currently {status})\n" +
             $"{connection.availability:P1} availability (target: {availability_:P1}) over {connection.days}/{connection.window} days\n" +
             $"(Availability yesterday: {connection.availability_yesterday:P1})";
      title_tracker_.Add(title);
      if (last_title_ != title) {
        title_tracker_.UpdateContractWindow(title);
      }
      last_title_ = title;
      return title;
    }

    private string connection_;
    private double availability_;
  }
}
