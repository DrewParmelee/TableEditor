A rating table will follow the following format:

<table name="tableName" delimiter=",">

    <rowKeys>
        <rowSet name="CoverageType" searchType="eq">
            <key>InstallationFloater</key>
            <key>RehabAndReno</key>
            <key>ScheduledJobsite</key>
        </rowSet>
        <rowSet name="CoverageDetailCoverageType" searchType="range">
            <key>SewerBackup</key>
            <key>AdditionalConstructionExpenses</key>
            <key>AdditionalSoftCost</key>
            <key>RentalIncome</key>
            <key>IncomeCov</key>
        </rowSet>
    </rowKeys>

    <colKeys>
        <colSet name="PerilType" searchType="eq">
            <key>BG1</key>
            <key>BG2</key>
            <key>OtherPerils</key>
            <key>AllRisk</key>
        </colSet>
    </colKeys>

    <pageKeys searchType="eq">
        <key>Page0</key>
    </pageKeys>

    <data>
        <row>-,-,-,0.920</row>
        <row>0.800,0.810,0.820,0.700</row>
        <row>0.830,0.840,0.850,0.710</row>
        <row>0.860,0.870,0.880,0.720</row>
        <row>0.890,0.900,0.910,0.730</row>
        <row>-,-,-,0.460</row>
        <row>0.930,0.940,0.950,-</row>
        <row>0.960,0.970,0.980,-</row>
        <row>0.990,0.400,0.410,-</row>
        <row>0.420,0.430,0.450,-</row>
        <row>-,-,-,0.630</row>
        <row>0.470,0.480,0.490,-</row>
        <row>0.510,0.520,0.530,-</row>
        <row>0.550,0.560,0.570,-</row>
        <row>0.590,0.600,0.610,-</row>
    </data>

</table>