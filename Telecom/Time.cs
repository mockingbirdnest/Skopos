using System;
using ContractConfigurator;
using Contracts;

namespace σκοπός {
  public static class RSS {
    public static readonly DateTime epoch = new DateTime(1951, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static DateTime current_time => epoch.AddSeconds(Telecom.Instance.last_universal_time);
  }

  public class BeforeDate : ContractRequirement {
    public override bool LoadFromConfig(ConfigNode node) {
      bool ok = base.LoadFromConfig(node);
      ok &= ConfigNodeUtil.ParseValue<DateTime>(node, "date", date => date_ = date, this);
      return ok;
    }

    public override void OnLoad(ConfigNode configNode) { }

    public override void OnSave(ConfigNode node) {
      node.AddValue("date", date_.ToString("O"));
    }

    public override bool RequirementMet(ConfiguredContract contractType) {
      return RSS.current_time < date_;
    }

    protected override string RequirementText() {
      return $"Before {date_:s}";
    }

    private DateTime date_;
  }

  public class WaitUntilDateFactory : ParameterFactory {
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      ok &= ConfigNodeUtil.ParseValue<DateTime>(node, "date", t => date_ = t, this);
      return ok;
    }

    public override ContractParameter Generate(Contract contract) {
      return new WaitUntilDate(date_);
    }

    private DateTime date_;
  }

  public class WaitUntilDate : ContractParameter {
    public WaitUntilDate() {}

    public WaitUntilDate(DateTime date) {
      date_ = date;
      disableOnStateChange = true;
    }

    protected override void OnUpdate() {
      base.OnUpdate();
      if (RSS.current_time >= date_) {
        SetComplete();
      } else {
        SetIncomplete();
      }
    }

    protected override void OnLoad(ConfigNode node) {
      date_ = DateTime.Parse(node.GetValue("date"));
    }

    protected override void OnSave(ConfigNode node) {
      node.AddValue("date", date_.ToString("O"));
    }

    protected override string GetTitle() {
      return $"Wait until {date_:s}";
    }

    private DateTime date_;
  }
}
