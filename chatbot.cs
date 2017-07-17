using System;
using System.IO;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

public class FixedQueue<T> {

	readonly int _size;
	Queue<String> q = new Queue<String>();

	public FixedQueue(int size) {
		_size = size;
	}

	public void Enqueue(string thing) {
		if ( q.Count() >= _size ) {
			q.Dequeue();
		}
		q.Enqueue(thing);
	}

	public bool Contains(string thing) {
		return q.Contains(thing);
	}

	public void Clear() {
		q.Clear();
	}

	public bool All(string thing) {
		if ( q.Count() != _size ) return false;
		foreach ( string x in q ) {
			if ( x != thing ) return false;
		}
		return true;
	}

}

public class ChatBot {
	/*Structure:
	 *Subject - Similar/Context - Related - Type of Response - Response
	 *Contexts: 1 - happy, 2 - neutral, 3 - sad
	 *Type of Response: 0 - Question, 1 - Reply/Initiative
	*/
	IEnumerable<XElement> src = XDocument.Load("actions.xml")
		         								 .Element("actions")
									 				 .Elements("subject");
	string[] positives;
	string[] negatives;
	string lastType;
	string lastSubject = "idle";
	Random rand = new Random();

	FixedQueue<string> said = new FixedQueue<string>(8);
	FixedQueue<string> subd = new FixedQueue<string>(4);

	int countOccur(string msg, string[] context){
		msg = new string(msg.ToCharArray()
								  .Where(x => x != '.' && x != '?' 
									  						  && x != '!')
								  .ToArray());
		return context.Where(word => 
				{	
					word = word.Replace('-', ' ');
					string pattern = @"(^|[^a-z]+)"
										  + word 
										  + @"([^a-z']+|$)";
					Regex rgx = new Regex(pattern, RegexOptions.IgnoreCase);
					return rgx.IsMatch(msg);
				})
				        .Count();
	}

	public string getContext(string msg) {

		int pos = countOccur(msg, positives);
		int neg = countOccur(msg, negatives);

		int density = pos + neg;

		if ( density == 0 ) return "norm";

		float posrate = (float)pos / (float)density;

		if ( posrate > 0.7 ) return "good";
		else if ( posrate < 0.3 ) return "bad";
		else return "norm";

	}


	public string getSubject(string msg) {
		var subject = src.Aggregate( (x, y) =>
				{
					string[] r1 = x.Element("related").Value.Split();
					string[] r2 = y.Element("related").Value.Split();
					if ( countOccur(msg, r1) >= countOccur(msg, r2) ) {
						return x;
					}
					else return y;
				});
		string topic = subject.Element("main").Value;  

		string[] it = {" it ", " it", "it "};

		if ( topic == "idle" && it.Any(msg.Contains) ) {
			return lastSubject;
		}
		return topic;
	}

	public string getType(string msg) {
		string[] questions = {"?", "what", "how", "why", "where", "when"};

		//Finding type
		if (questions.Any(msg.Contains) && lastType != "init") return "qu";
		else if (lastType == "init") return "re";

		Random rand = new Random();
		int pick = rand.Next(1, 3);
		
		if (pick == 1) return "init";
		return "re";
	}

	string getReply(string sub, string cont, string type) {
		lastSubject = sub;
		lastType = type;

		var stuff = src.Where(x => 
				x.Element("main").Value == sub)
							.Select(x => x);

		//Filter by those that aren't in previous, if not return everything
		
		var replies = stuff.Select(x =>
				x.Element(cont).Elements(type)).Single();

		if ( replies.Count() == 0 ) {
			replies = stuff.Select(x =>
					x.Descendants(type)).Single();
		}

		var nodup = replies.Where(x => !said.Contains(x.Value));

		if ( nodup.Count() == 0 ) said.Clear();	
		else replies = nodup;

		//Random from given replies

		var reply = replies.ElementAt(rand.Next(replies.Count()));
		said.Enqueue(reply.Value);

		return reply.Value;

	}

	public ChatBot() {
		//Loading context data 
		string[] seperate = {", ", "\n\n", "\n", " – "};
		StreamReader fs = new StreamReader("negatives2.txt");
		negatives = fs.ReadToEnd()
						  .ToLower()
						  .Split(seperate, StringSplitOptions.RemoveEmptyEntries)
						  .Distinct()
						  .ToArray();

		fs = new StreamReader("positives2.txt");
		positives = fs.ReadToEnd()
						  .ToLower()
						  .Split(seperate, StringSplitOptions.RemoveEmptyEntries)
						  .Distinct()
						  .ToArray();
	}

	public string[] getResponse(string msg) {
		msg = msg.ToLower();
		string subject = getSubject(msg);
		string context = getContext(msg);
		string curtype = getType(msg);

		//For following up on questions
		if ( subject == "idle" &&
				curtype != "qu" && !lastSubject.Contains("greet") )
			subject = lastSubject;
		if ( lastType == "init") {
			curtype = "fup";
			subject = lastSubject;
		}

		if ( subd.All(subject) && curtype != "fup") {
			var potQs = src.Where(x =>
					{
						string sub = x.Element("main").Value;	
						return !( sub.Contains("greet") ||
									 subd.Contains(sub) );
					})
								.Select(x => x);
				var next = potQs.ElementAt(rand.Next(potQs.Count()));
				subject = next.Element("main").Value;
			
		}

		subd.Enqueue(subject);
		
		string response = getReply(subject, context, curtype)
								+ "|";

		if ( curtype != "init" ) {
			//TODO: Check if anything's been asked in the current category yet
			
			string newtype = curtype;

			if ( curtype == "fup" && !subject.Contains("greet") ){
				newtype = "re";
			}
			else {
				var potQs = src.Where(x =>
					{
						string sub = x.Element("main").Value;	
						return !( sub.Contains("greet") ||
									 subd.Contains(sub) );
					})
								.Select(x => x);
				var next = potQs.ElementAt(rand.Next(potQs.Count()));
				subject = next.Element("main").Value;
			
				subd.Enqueue(subject);
				
				newtype = "init";
			}
			if( rand.Next(1, 4) == 2 ) { 
				subd.Enqueue(subject);
				response += getReply(subject, context, newtype);
			}
		}

		return response.Split(new[] {"|"}, 
				StringSplitOptions.RemoveEmptyEntries);
	}

}

class Chat {

	static void Main(string[] args) {
		var rand = new Random();

		ChatBot chatter = new ChatBot();
		
		string msg;

		Console.Write("You: ");

		while((msg = Console.ReadLine()) != null) {
			string[] responses = chatter.getResponse(msg);
			foreach(string line in responses) {
				int wait = rand.Next(5, 15);
				Thread.Sleep(wait * 100);
				Console.WriteLine("Leonbot: " + line);
			}
			Console.Write("You: ");
		}

	}

}
