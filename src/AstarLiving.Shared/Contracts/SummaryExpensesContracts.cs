using AstarLiving.Domain.Enums;

namespace AstarLiving.Shared.Contracts;

/// <summary>ยอดค้างของบ้านหนึ่งหลัง</summary>
public class SummaryExpensesHouseResponse
{
    public int ResidentId { get; init; }
    public string HouseNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    /// <summary>ค่าส่วนกลางหลังเฉลี่ยตามสัดส่วนที่ยังค้าง</summary>
    public decimal CommonFee { get; init; }
    public decimal Fine { get; init; }
    public decimal CollectionFee { get; init; }
    public decimal Total { get; init; }
    /// <summary>จำนวนงวดที่ค้าง</summary>
    public int PaymentCount { get; init; }
}

public class SummaryExpensesTotalResponse
{
    public decimal CommonFee { get; init; }
    public decimal Fine { get; init; }
    public decimal CollectionFee { get; init; }
    public decimal Total { get; init; }
    public int HouseCount { get; init; }
    public int PaymentCount { get; init; }
}

public class SummaryExpensesResponse
{
    public List<SummaryExpensesHouseResponse> ByHouse { get; init; } = [];
    public SummaryExpensesTotalResponse Total { get; init; } = new();
}

/// <summary>หนึ่งงวดในมุมมองรายบ้าน</summary>
public class SummaryExpensesMonthRow
{
    public int PaymentId { get; init; }
    public short Month { get; init; }
    public int Year { get; init; }
    public string InvNo { get; init; } = string.Empty;
    public decimal CommonFee { get; init; }
    public decimal Fine { get; init; }
    public decimal CollectionFee { get; init; }
    public decimal Total { get; init; }
    public PaymentStatus Status { get; init; }
}

/// <summary>งวดทั้งหมดของหนึ่งปี พ.ศ.</summary>
public class SummaryExpensesYearGroup
{
    public int Year { get; init; }
    public int MonthCount { get; init; }
    public decimal CommonFee { get; init; }
    public decimal Fine { get; init; }
    public decimal CollectionFee { get; init; }
    public decimal Total { get; init; }
    public List<SummaryExpensesMonthRow> Months { get; init; } = [];
}

public class SummaryExpensesResidentTotal
{
    public decimal CommonFee { get; init; }
    public decimal Fine { get; init; }
    public decimal CollectionFee { get; init; }
    public decimal Total { get; init; }
    public int PaymentCount { get; init; }
    public int YearCount { get; init; }
}

/// <summary>มุมมองรายบ้าน แยกตามปี พ.ศ. (ปีใหม่สุดอยู่บน)</summary>
public class SummaryExpensesByResidentResponse
{
    public int ResidentId { get; init; }
    public string HouseNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public List<SummaryExpensesYearGroup> ByYear { get; init; } = [];
    public SummaryExpensesResidentTotal Total { get; init; } = new();
}
