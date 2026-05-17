# 📊 ESAPI Plan Quality Metrics

## 📖 Overview
The **ESAPI Plan Quality Metrics** tool is an open-source automation script developed for the Varian Eclipse Treatment Planning System (TPS). 

Evaluating treatment plan quality manually involves reading Dose-Volume Histograms (DVH) to verify compliance with clinical protocols (e.g., V20Gy, D95%, Mean Dose). This manual process is time-consuming and prone to human transcription errors. This script automates the extraction and evaluation of these critical dosimetric metrics, providing a fast, objective, and standardized quality assurance (QA) audit for any radiotherapy plan.

## ✨ Key Features
* **Automated DVH Analysis:** Instantly extracts exact dose and volume metrics directly from the Eclipse calculation engine, eliminating manual curve interpolation.
* **Standardized Protocol Auditing:** Quickly compares plan results against established clinical constraints (e.g., target coverage, OAR maximums, and mean doses).
* **Multi-Metric Support:** Supports a wide variety of standard dosimetric queries, including $D_{x\%}$, $D_{xcc}$, $V_{xGy}$, $V_{x\%}$, $D_{max}$, and $D_{mean}$.
* **Clinical Efficiency:** Reduces plan evaluation time from several minutes of manual inspection to a few seconds of computational processing.

## 💻 System Requirements
* **Eclipse TPS:** Version 15.5 or higher.
* **.NET Framework:** Compatible with your clinic's specific ESAPI version (e.g., 4.5 for v15.6, or 4.6+ for v16+).

## 🛠️ Installation & Compilation
Depending on the specific UI components used in this project, it is highly recommended to compile it into a `.dll` library rather than running it as a standalone `.cs` file.

1. Clone or download this repository to your local machine.
2. Open the solution file (`.sln`) using **Visual Studio**.
3. In the Solution Explorer, restore any necessary NuGet packages.
4. Build the solution (`Ctrl + Shift + B` or `Build > Build Solution`).
5. Locate the compiled `.dll` file inside the `bin\Debug` or `bin\Release` folder.
6. In Eclipse, open the Script Runner, navigate to the folder containing your compiled `.dll`, and execute it.

## 🚀 How to Use
1. Open a Patient and calculate a Treatment Plan in Eclipse.
2. Ensure the plan is selected as the active context.
3. Run the compiled Plan Quality Metrics `.dll`.
4. Review the automatically generated scorecard/metrics report on your screen to verify clinical protocol compliance.

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## ⚠️ Clinical Disclaimer
**For Research and Educational Purposes Only.** This software is provided "as is", without warranty of any kind. It is the sole responsibility of the clinical user (Medical Physicist, Dosimetrist, or Radiation Oncologist) to strictly verify and validate all extracted metrics and dosimetric data against the native TPS DVH before making any clinical decisions or approving a patient's treatment plan.
