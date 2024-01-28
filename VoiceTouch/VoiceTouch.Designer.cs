﻿namespace VoiceTouch
{
    partial class VoiceTouch
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.comboBoxMidiInDevices = new System.Windows.Forms.ComboBox();
            this.comboBoxMidiOutDevices = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.progressLog1 = new NAudio.Utils.ProgressLog();
            this.buttonMonitor = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // comboBoxMidiInDevices
            // 
            this.comboBoxMidiInDevices.FormattingEnabled = true;
            this.comboBoxMidiInDevices.Location = new System.Drawing.Point(53, 12);
            this.comboBoxMidiInDevices.Name = "comboBoxMidiInDevices";
            this.comboBoxMidiInDevices.Size = new System.Drawing.Size(121, 21);
            this.comboBoxMidiInDevices.TabIndex = 0;
            this.comboBoxMidiInDevices.SelectedIndexChanged += new System.EventHandler(this.comboBoxMidiInDevices_SelectedIndexChanged);
            // 
            // comboBoxMidiOutDevices
            // 
            this.comboBoxMidiOutDevices.FormattingEnabled = true;
            this.comboBoxMidiOutDevices.Location = new System.Drawing.Point(53, 48);
            this.comboBoxMidiOutDevices.Name = "comboBoxMidiOutDevices";
            this.comboBoxMidiOutDevices.Size = new System.Drawing.Size(121, 21);
            this.comboBoxMidiOutDevices.TabIndex = 1;
            this.comboBoxMidiOutDevices.SelectedIndexChanged += new System.EventHandler(this.comboBoxMidiOutDevices_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(29, 15);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(18, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "IN";
            this.label1.Click += new System.EventHandler(this.label1_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(17, 51);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(30, 13);
            this.label2.TabIndex = 3;
            this.label2.Text = "OUT";
            // 
            // progressLog1
            // 
            this.progressLog1.BackColor = System.Drawing.SystemColors.ControlDarkDark;
            this.progressLog1.Location = new System.Drawing.Point(249, 12);
            this.progressLog1.Name = "progressLog1";
            this.progressLog1.Padding = new System.Windows.Forms.Padding(1);
            this.progressLog1.Size = new System.Drawing.Size(539, 426);
            this.progressLog1.TabIndex = 4;
            // 
            // buttonMonitor
            // 
            this.buttonMonitor.Location = new System.Drawing.Point(99, 97);
            this.buttonMonitor.Name = "buttonMonitor";
            this.buttonMonitor.Size = new System.Drawing.Size(75, 23);
            this.buttonMonitor.TabIndex = 6;
            this.buttonMonitor.Text = "Start";
            this.buttonMonitor.UseVisualStyleBackColor = true;
            this.buttonMonitor.Click += new System.EventHandler(this.buttonMonitor_Click);
            // 
            // VoiceTouch
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.buttonMonitor);
            this.Controls.Add(this.progressLog1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comboBoxMidiOutDevices);
            this.Controls.Add(this.comboBoxMidiInDevices);
            this.Name = "VoiceTouch";
            this.Text = "VoiceTouch";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ComboBox comboBoxMidiInDevices;
        private System.Windows.Forms.ComboBox comboBoxMidiOutDevices;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private NAudio.Utils.ProgressLog progressLog1;
        private System.Windows.Forms.Button buttonMonitor;
    }
}
