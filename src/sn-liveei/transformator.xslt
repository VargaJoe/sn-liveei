<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes" omit-xml-declaration="no"/>
  <xsl:strip-space elements="*"/>

  <!-- Identity transform: keep everything unchanged by default. -->
  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()"/>
    </xsl:copy>
  </xsl:template>

  <!-- Example customization: replace Password node with fixed value. -->
  <!-- <xsl:template match="Password">
	<Password>
      <Text>ZMEyZ1nPusYktTsvxXP1</Text>
    </Password>
  </xsl:template> -->
</xsl:stylesheet>
