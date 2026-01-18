# ESAPI Plan Quality Metrics

## Description
This tool automates the dosimetric evaluation of radiotherapy treatment plans (VMAT/IMRT) by calculating standard quality indices directly within Varian Eclipse. It eliminates manual DVH reading and ensures consistency in reporting.

## Key Features
* **Automated Indices:** Calculates:
  * **CI (Conformity Index):** Paddick / RTOG.
  * **HI (Homogeneity Index):** ICRU 83.
  * **GI (Gradient Index):** Low dose spillage evaluation.
* **DVH Mining:** Automatically extracts volume and dose statistics from the PlanSetup.
* **Report Generation:** Exports the results to a CSV format for clinical review or research.

## Technologies
* C# (.NET Framework)
* Varian ESAPI (Eclipse Scripting API)
* LINQ for data querying.

## Disclaimer
This tool is for research and educational purposes. Always verify results with approved clinical protocols.
