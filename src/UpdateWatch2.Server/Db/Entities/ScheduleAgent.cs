namespace UpdateWatch2.Server.Db.Entities;

/// <summary>
/// The fixed n:m membership between a <see cref="Schedule"/> and an
/// <see cref="Agent"/> — deliberately a static, admin-picked list rather
/// than a dynamic filter/criteria, per the design decision on
/// updatewatch2-server#25: a newly-registered agent is never automatically
/// swept into an existing schedule.
/// </summary>
public class ScheduleAgent
{
    public int ScheduleId { get; set; }

    public Schedule? Schedule { get; set; }

    public required string Hostname { get; set; }

    public Agent? Agent { get; set; }
}
