﻿using System.Collections.Generic;
using System.Xml.Linq;
using Vixen.Sys;

namespace Vixen.IO.Xml {
	class XmlProgramFilePolicy : ProgramFilePolicy {
		private Program _program;
		private XElement _content;

		private const string ELEMENT_PROGRAM = "Program";

		public XmlProgramFilePolicy() {
			// Used when wanting just the current version of the sequence file.
		}

		public XmlProgramFilePolicy(Program program, XElement content) {
			_program = program;
			_content = content;
		}

		protected override void WriteSequences() {
			XmlSequenceListSerializer sequenceListSerializer = new XmlSequenceListSerializer();
			XElement sequenceListElement = sequenceListSerializer.WriteObject(_program.Sequences);

			_content.Add(new XElement(ELEMENT_PROGRAM, sequenceListElement));
		}

		protected override void ReadSequences() {
			XmlSequenceListSerializer sequenceListSerializer = new XmlSequenceListSerializer();
			IEnumerable<ISequence> sequences = sequenceListSerializer.ReadObject(_content);

			_program.Sequences.Clear();
			_program.Sequences.AddRange(sequences);
		}
	}
}
