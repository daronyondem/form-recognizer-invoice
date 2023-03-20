using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace invoice_processor
{
    public class Invoice
    {
        public string InvoiceNumber { get; set; }
        public string PhysicalAddress { get; set; }
        public ElectricityDetails Electricity { get; set; }

        public class ElectricityDetails
        {
            public double StartReading { get; set; }
            public double EndReading { get; set; }
            public double ActualReading { get; set; }
            public double DailyAverageConsumption { get; set; }
        }
    }

}
